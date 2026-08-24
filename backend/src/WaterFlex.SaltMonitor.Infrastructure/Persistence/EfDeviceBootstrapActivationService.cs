using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WaterFlex.SaltMonitor.Provisioning;

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence;

public sealed class EfDeviceBootstrapActivationService(
    SaltMonitorDbContext dbContext,
    TimeProvider timeProvider) : IDeviceBootstrapActivationService
{
    public async Task<ActivationResult> ActivateAsync(
        string bootstrapToken,
        ActivateDeviceRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(request);
        var validationErrors = Validate(normalized);
        if (validationErrors.Count > 0)
        {
            return ActivationResult.Failed(ActivationFailure.InvalidRequest, validationErrors);
        }

        if (!TryParseToken(bootstrapToken, out var credentialId, out var secret))
        {
            return ActivationResult.Failed(ActivationFailure.InvalidBootstrapToken);
        }

        var operationalSecretHash = TryDecodeHash(normalized.OperationalSecretHash, out var hashBytes)
            ? hashBytes
            : null;
        if (operationalSecretHash is null)
        {
            return ActivationResult.Failed(
                ActivationFailure.InvalidRequest,
                [new(nameof(request.OperationalSecretHash), "Operational secret hash must be Base64 for a 32-byte SHA-256 hash.")]);
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

                var now = timeProvider.GetUtcNow();
                var bootstrapCredential = await dbContext.DeviceBootstrapCredentials
                    .Include(candidate => candidate.Device)
                    .SingleOrDefaultAsync(
                        candidate => candidate.CredentialId == credentialId,
                        cancellationToken);

                if (bootstrapCredential is null)
                {
                    return ActivationResult.Failed(ActivationFailure.InvalidBootstrapToken);
                }

                var presentedHash = SHA256.HashData(secret);
                if (!CryptographicOperations.FixedTimeEquals(bootstrapCredential.SecretHash, presentedHash))
                {
                    bootstrapCredential.FailedAttemptCount += 1;
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return ActivationResult.Failed(ActivationFailure.InvalidBootstrapToken);
                }

                if (bootstrapCredential.RevokedAtUtc is not null
                    || bootstrapCredential.ConsumedAtUtc is not null
                    || bootstrapCredential.ValidFromUtc > now
                    || (bootstrapCredential.ExpiresAtUtc is { } expiresAt && expiresAt <= now))
                {
                    return ActivationResult.Failed(ActivationFailure.BootstrapUnavailable);
                }

                var device = bootstrapCredential.Device;
                if (device.SerialNumber != normalized.SerialNumber
                    || device.HardwareId != normalized.HardwareId)
                {
                    return ActivationResult.Failed(
                        ActivationFailure.InvalidRequest,
                        [new(nameof(request.SerialNumber), "SerialNumber and HardwareId must match the bootstrap credential device.")]);
                }

                var session = await dbContext.CommissioningSessions
                    .Include(candidate => candidate.Tank)
                    .ThenInclude(tank => tank.Installations)
                    .SingleOrDefaultAsync(
                        candidate => candidate.DeviceId == device.Id
                            && candidate.Status == CommissioningSessionStatus.PendingSensor,
                        cancellationToken);

                if (session is null || session.ExpiresAtUtc <= now)
                {
                    return ActivationResult.Failed(ActivationFailure.NoPendingCommissioning);
                }

                var existingByAttempt = await dbContext.CommissioningSessions
                    .Include(candidate => candidate.ProvisionalCredential)
                    .Include(candidate => candidate.Device)
                    .SingleOrDefaultAsync(
                        candidate => candidate.ActivationAttemptId == normalized.ActivationAttemptId,
                        cancellationToken);

                if (existingByAttempt is not null)
                {
                    if (existingByAttempt.Id != session.Id
                        || existingByAttempt.ProvisionalCredential is null
                        || existingByAttempt.ProvisionalCredential.CredentialId != normalized.OperationalCredentialId
                        || !CryptographicOperations.FixedTimeEquals(
                            existingByAttempt.ProvisionalCredential.SecretHash,
                            operationalSecretHash))
                    {
                        return ActivationResult.Failed(ActivationFailure.ActivationAttemptMismatch);
                    }

                    var existingInstallation = await dbContext.DeviceInstallations
                        .SingleAsync(
                            installation => installation.DeviceId == existingByAttempt.DeviceId
                                && installation.RemovedAtUtc == null,
                            cancellationToken);

                    return ActivationResult.Success(new(
                        existingByAttempt.DeviceId,
                        existingInstallation.Id,
                        existingByAttempt.ProvisionalCredential.CredentialId,
                        existingByAttempt.ActivatedAtUtc ?? now,
                        "already_activated"));
                }

                if (await dbContext.DeviceCredentials.AnyAsync(
                        credential => credential.CredentialId == normalized.OperationalCredentialId,
                        cancellationToken))
                {
                    return ActivationResult.Failed(ActivationFailure.ActivationConflict);
                }

                if (await dbContext.DeviceInstallations.AnyAsync(
                        installation => installation.DeviceId == device.Id && installation.RemovedAtUtc == null,
                        cancellationToken))
                {
                    return ActivationResult.Failed(ActivationFailure.ActivationConflict);
                }

                if (await dbContext.DeviceInstallations.AnyAsync(
                        installation => installation.TankId == session.TankId && installation.RemovedAtUtc == null,
                        cancellationToken))
                {
                    return ActivationResult.Failed(ActivationFailure.ActivationConflict);
                }

                var installation = new DeviceInstallation
                {
                    Id = Guid.NewGuid(),
                    DeviceId = device.Id,
                    TankId = session.TankId,
                    DealerId = session.DealerId,
                    InstalledAtUtc = now,
                    InstalledBy = session.CreatedByDisplayName,
                    WaterFlexWorkOrderId = session.WaterFlexWorkOrderId
                };

                var commissioningDistanceMm = normalized.CommissioningDistanceMm ?? session.TankDepthMm;
                if (commissioningDistanceMm > session.TankDepthMm)
                {
                    return ActivationResult.Failed(
                        ActivationFailure.InvalidRequest,
                        [new(nameof(request.CommissioningDistanceMm), "Commissioning distance cannot exceed tank depth.")]);
                }

                var calibration = new TankCalibrationRecord
                {
                    Id = Guid.NewGuid(),
                    DeviceInstallationId = installation.Id,
                    Version = 1,
                    TankDepthMm = session.TankDepthMm,
                    CommissioningDistanceMm = commissioningDistanceMm,
                    EffectiveFromUtc = now,
                    CreatedBy = "bootstrap_activation",
                    CreatedAtUtc = now
                };

                var deviceCredential = new DeviceCredential
                {
                    Id = Guid.NewGuid(),
                    DeviceId = device.Id,
                    CredentialId = normalized.OperationalCredentialId,
                    SecretHash = operationalSecretHash,
                    ValidFromUtc = now
                };

                session.ProvisionalCredentialId = deviceCredential.Id;
                session.ActivationAttemptId = normalized.ActivationAttemptId;
                session.Status = CommissioningSessionStatus.Completed;
                session.ActivatedAtUtc = now;
                session.CompletedAtUtc = now;

                bootstrapCredential.LastUsedAtUtc = now;
                bootstrapCredential.ConsumedAtUtc = now;
                bootstrapCredential.FailedAttemptCount = 0;

                device.Status = DeviceLifecycleStatus.Active;
                device.CommissionedAtUtc = now;

                var auditEvent = new ProvisioningAuditEvent
                {
                    DeviceId = device.Id,
                    CommissioningSessionId = session.Id,
                    EventType = "device_bootstrap_activated",
                    ActorType = "device",
                    ActorId = bootstrapCredential.CredentialId,
                    DetailsJson = JsonSerializer.Serialize(new
                    {
                        normalized.FirmwareVersion,
                        normalized.ConfigurationVersion,
                        normalized.OperationalCredentialId,
                        normalized.ActivationAttemptId
                    }),
                    OccurredAtUtc = now
                };

                dbContext.AddRange(installation, calibration, deviceCredential, auditEvent);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return ActivationResult.Success(new(
                    device.Id,
                    installation.Id,
                    deviceCredential.CredentialId,
                    now,
                    "activated"));
            });
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            return ActivationResult.Failed(ActivationFailure.Conflict);
        }
    }

    private static ActivateDeviceRequest Normalize(ActivateDeviceRequest request) =>
        request with
        {
            SerialNumber = request.SerialNumber.Trim().ToUpperInvariant(),
            HardwareId = new string(request.HardwareId
                .Where(character => !char.IsWhiteSpace(character) && character is not ':' and not '-')
                .Select(char.ToUpperInvariant)
                .ToArray()),
            FirmwareVersion = request.FirmwareVersion.Trim(),
            ConfigurationVersion = request.ConfigurationVersion.Trim(),
            OperationalCredentialId = request.OperationalCredentialId.Trim(),
            OperationalSecretHash = request.OperationalSecretHash.Trim()
        };

    private static IReadOnlyList<ProvisioningValidationError> Validate(ActivateDeviceRequest request)
    {
        var errors = new List<ProvisioningValidationError>();
        Require(request.SerialNumber, nameof(request.SerialNumber), 64, errors);
        Require(request.HardwareId, nameof(request.HardwareId), 32, errors);
        Require(request.FirmwareVersion, nameof(request.FirmwareVersion), 64, errors);
        Require(request.ConfigurationVersion, nameof(request.ConfigurationVersion), 64, errors);
        Require(request.OperationalCredentialId, nameof(request.OperationalCredentialId), 64, errors);
        Require(request.OperationalSecretHash, nameof(request.OperationalSecretHash), 128, errors);

        if (request.ActivationAttemptId == Guid.Empty)
        {
            errors.Add(new(nameof(request.ActivationAttemptId), "ActivationAttemptId is required."));
        }

        if (request.SerialNumber.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            errors.Add(new(nameof(request.SerialNumber), "Serial number must contain letters, numbers, or hyphens."));
        }

        if (request.HardwareId.Length != 12 || request.HardwareId.Any(character => !Uri.IsHexDigit(character)))
        {
            errors.Add(new(nameof(request.HardwareId), "Hardware ID must be a 12-character ESP32 hexadecimal ID."));
        }

        if (!request.OperationalCredentialId.StartsWith("wf_dev_", StringComparison.Ordinal)
            || request.OperationalCredentialId.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-'))
        {
            errors.Add(new(nameof(request.OperationalCredentialId), "OperationalCredentialId must begin with wf_dev_ and contain only letters, numbers, underscores, or hyphens."));
        }

        if (request.CommissioningDistanceMm is < 30 or > 4500)
        {
            errors.Add(new(nameof(request.CommissioningDistanceMm), "Commissioning distance must be between 30 and 4500 millimeters."));
        }

        return errors;
    }

    private static bool TryParseToken(string token, out string credentialId, out byte[] secret)
    {
        credentialId = string.Empty;
        secret = [];

        var separatorIndex = token.IndexOf('.');
        if (separatorIndex is <= 0 || separatorIndex == token.Length - 1)
        {
            return false;
        }

        credentialId = token[..separatorIndex].Trim();
        return credentialId.Length <= 64 && TryDecodeBase64UrlSecret(token[(separatorIndex + 1)..].Trim(), out secret);
    }

    private static bool TryDecodeBase64UrlSecret(string value, out byte[] secret)
    {
        secret = [];
        if (value.Length is < 42 or > 44)
        {
            return false;
        }

        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');

        try
        {
            secret = Convert.FromBase64String(base64);
            return secret.Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool TryDecodeHash(string base64Hash, out byte[] hashBytes)
    {
        hashBytes = [];

        try
        {
            hashBytes = Convert.FromBase64String(base64Hash);
            return hashBytes.Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static void Require(
        string value,
        string field,
        int maximumLength,
        ICollection<ProvisioningValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
        {
            errors.Add(new(field, $"{field} is required and cannot exceed {maximumLength} characters."));
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: "23505" };
}
