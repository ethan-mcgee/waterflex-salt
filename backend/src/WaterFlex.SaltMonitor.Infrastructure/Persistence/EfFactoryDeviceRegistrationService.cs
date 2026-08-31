using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WaterFlex.SaltMonitor.Provisioning;

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence;

public sealed class EfFactoryDeviceRegistrationService(
    SaltMonitorDbContext dbContext,
    TimeProvider timeProvider) : IFactoryDeviceRegistrationService
{
    public async Task<FactoryRegistrationResult> RegisterAsync(
        RegisterFactoryDeviceRequest request,
        string factoryOperatorId,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(request);
        var validationErrors = Validate(normalized, factoryOperatorId);
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
                    await transaction.CommitAsync(cancellationToken);
                    return FactoryRegistrationResult.Success(ToRegistration(
                        existing.Device,
                        existing.Device.BootstrapCredentials.Single()));
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
                    FactoryProvisionedBy = factoryOperatorId.Trim()
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
                    CreatedBy = factoryOperatorId.Trim(),
                    CreatedAtUtc = now
                };
                var auditEvent = new ProvisioningAuditEvent
                {
                    DeviceId = device.Id,
                    EventType = "factory_device_registered",
                    ActorType = "factory",
                    ActorId = factoryOperatorId.Trim(),
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
                await transaction.CommitAsync(cancellationToken);
                return FactoryRegistrationResult.Success(ToRegistration(device, credential));
            });
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            dbContext.ChangeTracker.Clear();
            return await FindByIdempotencyKeyAsync(normalized.IdempotencyKey, factoryOperatorId, cancellationToken);
        }
    }

    public async Task<FactoryRegistrationResult> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        string factoryOperatorId,
        CancellationToken cancellationToken = default)
    {
        var normalizedKey = idempotencyKey.Trim();
        if (normalizedKey.Length is < 8 or > 100 || string.IsNullOrWhiteSpace(factoryOperatorId))
        {
            return FactoryRegistrationResult.Failed(FactoryRegistrationFailure.InvalidRequest);
        }
        var job = await dbContext.FactoryProvisioningJobs
            .AsNoTracking()
            .Include(candidate => candidate.Device)
            .ThenInclude(device => device.BootstrapCredentials)
            .SingleOrDefaultAsync(candidate => candidate.IdempotencyKey == normalizedKey, cancellationToken);
        return job is null
            ? FactoryRegistrationResult.Failed(FactoryRegistrationFailure.DeviceAlreadyRegistered)
            : FactoryRegistrationResult.Success(ToRegistration(job.Device, job.Device.BootstrapCredentials.Single()));
    }

    public async Task<FactoryVerificationResult> RecordVerificationAsync(
        Guid deviceId,
        FactoryVerificationRequest request,
        string factoryOperatorId,
        CancellationToken cancellationToken = default)
    {
        var job = await dbContext.FactoryProvisioningJobs
            .Include(candidate => candidate.Device)
            .SingleOrDefaultAsync(candidate => candidate.DeviceId == deviceId, cancellationToken)
            ?? throw new KeyNotFoundException("Factory provisioning job was not found.");
        var passed = request.FirmwareVerified && request.IdentityVerified
            && request.PortalVerified && request.SensorVerified
            && string.Equals(request.FirmwareVersion.Trim(), job.Device.FactoryFirmwareVersion, StringComparison.Ordinal);
        var now = timeProvider.GetUtcNow();
        job.Status = passed ? FactoryProvisioningStatus.Provisioned : FactoryProvisioningStatus.Failed;
        job.VerifiedAtUtc = now;
        job.FailureCode = passed ? null : NormalizeFailureCode(request.FailureCode);
        dbContext.ProvisioningAuditEvents.Add(new()
        {
            DeviceId = deviceId,
            EventType = passed ? "factory_verification_passed" : "factory_verification_failed",
            ActorType = "factory",
            ActorId = factoryOperatorId.Trim(),
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
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(deviceId, job.SerialNumber, job.Status, now, job.FailureCode);
    }

    private static FactoryDeviceRegistration ToRegistration(Device device, DeviceBootstrapCredential credential) =>
        new(device.Id, device.SerialNumber, device.Model, device.RegisteredAtUtc, credential.CredentialId);

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
