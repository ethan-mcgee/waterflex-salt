using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WaterFlex.SaltMonitor.Domain.Security;
using WaterFlex.SaltMonitor.Provisioning;

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence;

/// <summary>
/// EF-backed implementation of factory-floor device registration and end-of-line verification.
/// Assigns the next sequential factory serial number under a table-level lock so concurrent
/// factory registrations cannot race onto the same sequence value, and treats a retried
/// registration with an already-seen idempotency key as a success returning the original device
/// rather than an error, so a factory tool retry after a dropped connection is safe.
/// </summary>
public sealed class EfFactoryDeviceRegistrationService(
    SaltMonitorDbContext dbContext,
    TimeProvider timeProvider) : IFactoryDeviceRegistrationService
{
    public async Task<FactoryRegistrationResult> RegisterAsync(
        RegisterFactoryDeviceRequest request,
        StaffActor factoryOperator,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(request);
        var validationErrors = Validate(normalized, factoryOperator.UserId);
        if (validationErrors.Count > 0)
        {
            return FactoryRegistrationResult.Failed(FactoryRegistrationFailure.InvalidRequest, validationErrors);
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
                await dbContext.Database.ExecuteSqlRawAsync(
                    "LOCK TABLE \"FactoryProvisioningJobs\" IN EXCLUSIVE MODE",
                    cancellationToken);
                var existing = await dbContext.FactoryProvisioningJobs
                    .Include(job => job.Device)
                    .ThenInclude(device => device.BootstrapCredentials)
                    .SingleOrDefaultAsync(job => job.IdempotencyKey == normalized.IdempotencyKey, cancellationToken);
                if (existing is not null)
                {
                    if (!CanAccess(existing, factoryOperator))
                    {
                        await transaction.CommitAsync(cancellationToken);
                        return FactoryRegistrationResult.Failed(FactoryRegistrationFailure.DeviceAlreadyRegistered);
                    }
                    var existingToken = await EnsureFlashAuthorizationAsync(existing, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return FactoryRegistrationResult.Success(ToRegistration(existing, existingToken));
                }

                if (await dbContext.DeviceBootstrapCredentials.AnyAsync(
                    credential => credential.CredentialId == normalized.BootstrapCredentialId,
                    cancellationToken))
                {
                    return FactoryRegistrationResult.Failed(
                        FactoryRegistrationFailure.BootstrapCredentialAlreadyRegistered);
                }

                var nextSequence = (await dbContext.FactoryProvisioningJobs
                    .MaxAsync(job => (long?)job.SerialSequence, cancellationToken) ?? 0L) + 1L;
                var serialNumber = FactorySerialNumber.Format(nextSequence);
                var now = timeProvider.GetUtcNow();
                var device = new Device
                {
                    Id = Guid.NewGuid(),
                    SerialNumber = serialNumber,
                    Model = normalized.Model,
                    Status = DeviceLifecycleStatus.Registered,
                    RegisteredAtUtc = now,
                    FactoryFirmwareVersion = normalized.FirmwareVersion,
                    FactoryConfigurationVersion = normalized.ConfigurationVersion,
                    FactoryProvisionedBy = factoryOperator.UserId
                };
                var credential = new DeviceBootstrapCredential
                {
                    Id = Guid.NewGuid(),
                    DeviceId = device.Id,
                    CredentialId = normalized.BootstrapCredentialId,
                    SecretHash = Convert.FromBase64String(normalized.BootstrapSecretHash),
                    ValidFromUtc = now
                };
                var job = new FactoryProvisioningJob
                {
                    Id = Guid.NewGuid(),
                    IdempotencyKey = normalized.IdempotencyKey,
                    SerialSequence = nextSequence,
                    SerialNumber = serialNumber,
                    Status = FactoryProvisioningStatus.Registered,
                    DeviceId = device.Id,
                    CreatedBy = factoryOperator.UserId,
                    CreatedAtUtc = now
                };
                job.Device = device;
                device.FactoryProvisioningJob = job;
                credential.Device = device;
                device.BootstrapCredentials.Add(credential);
                var auditEvent = new ProvisioningAuditEvent
                {
                    DeviceId = device.Id,
                    EventType = "factory_device_registered",
                    ActorType = "staff",
                    ActorId = factoryOperator.UserId,
                    DetailsJson = JsonSerializer.Serialize(new
                    {
                        normalized.IdempotencyKey,
                        SerialSequence = nextSequence,
                        normalized.FirmwareVersion,
                        normalized.ConfigurationVersion
                    }),
                    OccurredAtUtc = now
                };

                dbContext.AddRange(device, credential, job, auditEvent);
                await dbContext.SaveChangesAsync(cancellationToken);
                var token = await EnsureFlashAuthorizationAsync(job, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return FactoryRegistrationResult.Success(ToRegistration(job, token));
            });
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            dbContext.ChangeTracker.Clear();
            return await FindByIdempotencyKeyAsync(normalized.IdempotencyKey, factoryOperator, cancellationToken);
        }
    }

    public async Task<FactoryRegistrationResult> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        StaffActor factoryOperator,
        CancellationToken cancellationToken = default)
    {
        var normalizedKey = idempotencyKey.Trim();
        if (normalizedKey.Length is < 8 or > 100 || string.IsNullOrWhiteSpace(factoryOperator.UserId))
        {
            return FactoryRegistrationResult.Failed(FactoryRegistrationFailure.InvalidRequest);
        }
        var job = await dbContext.FactoryProvisioningJobs
            .AsNoTracking()
            .Include(candidate => candidate.Device)
            .ThenInclude(device => device.BootstrapCredentials)
            .SingleOrDefaultAsync(candidate => candidate.IdempotencyKey == normalizedKey, cancellationToken);
        if (job is null || !CanAccess(job, factoryOperator))
        {
            return FactoryRegistrationResult.Failed(FactoryRegistrationFailure.DeviceAlreadyRegistered);
        }
        var token = await EnsureFlashAuthorizationAsync(job, cancellationToken);
        return FactoryRegistrationResult.Success(ToRegistration(job, token));
    }

    public async Task<FactoryRegistrationResult> FindActiveByOperatorAsync(
        StaffActor factoryOperator,
        CancellationToken cancellationToken = default)
    {
        var job = await dbContext.FactoryProvisioningJobs
            .AsNoTracking()
            .Include(candidate => candidate.Device)
            .ThenInclude(device => device.BootstrapCredentials)
            .Where(candidate => candidate.CreatedBy == factoryOperator.UserId
                && (candidate.Status == FactoryProvisioningStatus.Registered
                    || candidate.Status == FactoryProvisioningStatus.Quarantined))
            .OrderByDescending(candidate => candidate.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (job is null)
        {
            return FactoryRegistrationResult.Failed(FactoryRegistrationFailure.DeviceAlreadyRegistered);
        }
        var token = await EnsureFlashAuthorizationAsync(job, cancellationToken);
        return FactoryRegistrationResult.Success(ToRegistration(job, token));
    }

    public async Task<FactoryVerificationResult> RecordVerificationAsync(
        Guid deviceId,
        FactoryVerificationRequest request,
        StaffActor factoryOperator,
        CancellationToken cancellationToken = default)
    {
        var job = await dbContext.FactoryProvisioningJobs
            .Include(candidate => candidate.Device)
            .SingleOrDefaultAsync(candidate => candidate.DeviceId == deviceId, cancellationToken)
            ?? throw new KeyNotFoundException("Factory provisioning job was not found.");
        if (!CanAccess(job, factoryOperator))
        {
            throw new KeyNotFoundException("Factory provisioning job was not found.");
        }
        if (job.Status == FactoryProvisioningStatus.Provisioned)
        {
            throw new InvalidOperationException("A provisioned factory job cannot be changed.");
        }
        if (job.Status == FactoryProvisioningStatus.Quarantined)
        {
            throw new InvalidOperationException("Retry the quarantined job before submitting new verification evidence.");
        }
        var passed = request.FirmwareVerified && request.IdentityVerified
            && request.PortalVerified && request.SensorVerified
            && string.Equals(request.FirmwareVersion.Trim(), job.Device.FactoryFirmwareVersion, StringComparison.Ordinal);
        var now = timeProvider.GetUtcNow();
        job.Status = passed ? FactoryProvisioningStatus.Provisioned : FactoryProvisioningStatus.Quarantined;
        job.VerifiedAtUtc = now;
        job.FailureCode = passed ? null : NormalizeFailureCode(request.FailureCode);
        dbContext.ProvisioningAuditEvents.Add(new()
        {
            DeviceId = deviceId,
            EventType = passed ? "factory_verification_passed" : "factory_verification_failed",
            ActorType = "staff",
            ActorId = factoryOperator.UserId,
            DetailsJson = JsonSerializer.Serialize(new
            {
                request.FirmwareVerified,
                request.IdentityVerified,
                request.PortalVerified,
                request.SensorVerified,
                job.FailureCode
            }),
            OccurredAtUtc = now
        });
        await RevokeLiveFlashAuthorizationsAsync(job.Id, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(deviceId, job.SerialNumber, job.Status, now, job.FailureCode);
    }

    public async Task<FactoryDeviceRegistration> RetryAsync(
        Guid deviceId,
        StaffActor factoryOperator,
        CancellationToken cancellationToken = default)
    {
        var job = await dbContext.FactoryProvisioningJobs
            .Include(candidate => candidate.Device)
            .ThenInclude(device => device.BootstrapCredentials)
            .SingleOrDefaultAsync(candidate => candidate.DeviceId == deviceId, cancellationToken)
            ?? throw new KeyNotFoundException("Factory provisioning job was not found.");
        if (!CanAccess(job, factoryOperator))
        {
            throw new KeyNotFoundException("Factory provisioning job was not found.");
        }
        if (job.Status == FactoryProvisioningStatus.Provisioned)
        {
            throw new InvalidOperationException("A provisioned factory job cannot be retried.");
        }
        if (job.Status != FactoryProvisioningStatus.Quarantined)
        {
            throw new InvalidOperationException("Only a quarantined factory job can be retried.");
        }

        var now = timeProvider.GetUtcNow();
        var previousStatus = job.Status;
        job.Status = FactoryProvisioningStatus.Registered;
        job.VerifiedAtUtc = null;
        job.FailureCode = null;
        dbContext.ProvisioningAuditEvents.Add(new()
        {
            DeviceId = deviceId,
            EventType = "factory_provisioning_retried",
            ActorType = "staff",
            ActorId = factoryOperator.UserId,
            DetailsJson = JsonSerializer.Serialize(new { PreviousStatus = previousStatus }),
            OccurredAtUtc = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        var token = await EnsureFlashAuthorizationAsync(job, cancellationToken);
        return ToRegistration(job, token);
    }

    private static bool CanAccess(FactoryProvisioningJob job, StaffActor actor) =>
        actor.Role == StaffRole.WaterFlexAdministrator
        || string.Equals(job.CreatedBy, actor.UserId, StringComparison.Ordinal);

    /// <summary>
    /// Mints a fresh single-use flash-authorization token for a <see cref="FactoryProvisioningStatus.Registered"/>
    /// job, revoking any prior unconsumed one for the same job so a stale token can never be replayed
    /// once a newer one has been issued. Returns null for a job that isn't Registered — a Quarantined
    /// or Provisioned job has no business authorizing a flash.
    /// </summary>
    private async Task<string?> EnsureFlashAuthorizationAsync(
        FactoryProvisioningJob job,
        CancellationToken cancellationToken)
    {
        if (job.Status != FactoryProvisioningStatus.Registered)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        await RevokeLiveFlashAuthorizationsAsync(job.Id, now, cancellationToken);

        var secret = RandomNumberGenerator.GetBytes(32);
        var authorization = new FactoryFlashAuthorization
        {
            Id = Guid.NewGuid(),
            FactoryProvisioningJobId = job.Id,
            DeviceId = job.DeviceId,
            CredentialId = $"wf_flash_{Guid.NewGuid():N}",
            SecretHash = SHA256.HashData(secret),
            IssuedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(10)
        };
        dbContext.FactoryFlashAuthorizations.Add(authorization);
        await dbContext.SaveChangesAsync(cancellationToken);
        return $"{authorization.CredentialId}.{Base64Url(secret)}";
    }

    private async Task RevokeLiveFlashAuthorizationsAsync(
        Guid factoryProvisioningJobId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var live = await dbContext.FactoryFlashAuthorizations
            .Where(candidate => candidate.FactoryProvisioningJobId == factoryProvisioningJobId
                && candidate.ConsumedAtUtc == null
                && candidate.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var authorization in live)
        {
            authorization.RevokedAtUtc = now;
        }
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static FactoryDeviceRegistration ToRegistration(FactoryProvisioningJob job, string? flashAuthorizationToken) =>
        new(
            job.Device.Id,
            job.IdempotencyKey,
            job.Device.SerialNumber,
            job.Device.Model,
            job.Device.RegisteredAtUtc,
            job.Device.BootstrapCredentials.Single().CredentialId,
            job.Status,
            job.VerifiedAtUtc,
            job.FailureCode,
            flashAuthorizationToken);

    private static RegisterFactoryDeviceRequest Normalize(RegisterFactoryDeviceRequest request) => request with
    {
        IdempotencyKey = request.IdempotencyKey.Trim(),
        Model = request.Model.Trim(),
        BootstrapCredentialId = request.BootstrapCredentialId.Trim(),
        BootstrapSecretHash = request.BootstrapSecretHash.Trim(),
        FirmwareVersion = request.FirmwareVersion.Trim(),
        ConfigurationVersion = request.ConfigurationVersion.Trim()
    };

    private static IReadOnlyList<ProvisioningValidationError> Validate(
        RegisterFactoryDeviceRequest request,
        string factoryOperatorId)
    {
        var errors = new List<ProvisioningValidationError>();
        Require(request.IdempotencyKey, nameof(request.IdempotencyKey), 100, errors);
        Require(request.Model, nameof(request.Model), 100, errors);
        Require(request.BootstrapCredentialId, nameof(request.BootstrapCredentialId), 64, errors);
        Require(request.FirmwareVersion, nameof(request.FirmwareVersion), 64, errors);
        Require(request.ConfigurationVersion, nameof(request.ConfigurationVersion), 64, errors);
        Require(factoryOperatorId?.Trim() ?? string.Empty, "FactoryOperatorId", 200, errors);
        if (request.IdempotencyKey.Length < 8)
        {
            errors.Add(new(nameof(request.IdempotencyKey), "Idempotency key must contain at least 8 characters."));
        }
        if (!request.BootstrapCredentialId.StartsWith("wf_boot_", StringComparison.Ordinal)
            || request.BootstrapCredentialId.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-'))
        {
            errors.Add(new(nameof(request.BootstrapCredentialId),
                "Bootstrap credential ID must begin with wf_boot_ and contain only letters, numbers, underscores, or hyphens."));
        }
        try
        {
            if (Convert.FromBase64String(request.BootstrapSecretHash).Length != 32)
            {
                errors.Add(new(nameof(request.BootstrapSecretHash),
                    "Bootstrap secret hash must be a Base64-encoded 32-byte SHA-256 value."));
            }
        }
        catch (FormatException)
        {
            errors.Add(new(nameof(request.BootstrapSecretHash), "Bootstrap secret hash must be valid Base64."));
        }
        return errors;
    }

    private static void Require(string value, string field, int maximumLength, ICollection<ProvisioningValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
        {
            errors.Add(new(field, $"{field} is required and cannot exceed {maximumLength} characters."));
        }
    }

    private static string NormalizeFailureCode(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "factory_verification_failed" : value.Trim();
        return normalized[..Math.Min(100, normalized.Length)];
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: "23505" };
}
