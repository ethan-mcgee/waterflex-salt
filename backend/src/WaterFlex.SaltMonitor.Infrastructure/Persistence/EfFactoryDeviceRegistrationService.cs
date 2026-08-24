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
            return FactoryRegistrationResult.Failed(
                FactoryRegistrationFailure.InvalidRequest,
                validationErrors);
        }

        var secretHash = Convert.FromBase64String(normalized.BootstrapSecretHash);
        var strategy = dbContext.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

                if (await dbContext.Devices.AnyAsync(
                    device => device.SerialNumber == normalized.SerialNumber
                        || device.HardwareId == normalized.HardwareId,
                    cancellationToken))
                {
                    return FactoryRegistrationResult.Failed(
                        FactoryRegistrationFailure.DeviceAlreadyRegistered);
                }

                if (await dbContext.DeviceBootstrapCredentials.AnyAsync(
                    credential => credential.CredentialId == normalized.BootstrapCredentialId,
                    cancellationToken))
                {
                    return FactoryRegistrationResult.Failed(
                        FactoryRegistrationFailure.BootstrapCredentialAlreadyRegistered);
                }

                var now = timeProvider.GetUtcNow();
                var device = new Device
                {
                    Id = Guid.NewGuid(),
                    SerialNumber = normalized.SerialNumber,
                    HardwareId = normalized.HardwareId,
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
                    SecretHash = secretHash,
                    ValidFromUtc = now
                };
                var auditEvent = new ProvisioningAuditEvent
                {
                    DeviceId = device.Id,
                    EventType = "factory_device_registered",
                    ActorType = "factory",
                    ActorId = factoryOperatorId.Trim(),
                    DetailsJson = JsonSerializer.Serialize(new
                    {
                        normalized.FirmwareVersion,
                        normalized.ConfigurationVersion
                    }),
                    OccurredAtUtc = now
                };

                dbContext.AddRange(device, credential, auditEvent);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return FactoryRegistrationResult.Success(new(
                    device.Id,
                    device.SerialNumber,
                    device.HardwareId,
                    device.Model,
                    device.RegisteredAtUtc,
                    credential.CredentialId));
            });
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            return FactoryRegistrationResult.Failed(FactoryRegistrationFailure.Conflict);
        }
    }

    private static RegisterFactoryDeviceRequest Normalize(RegisterFactoryDeviceRequest request) =>
        request with
        {
            SerialNumber = request.SerialNumber.Trim().ToUpperInvariant(),
            HardwareId = new string(request.HardwareId
                .Where(character => !char.IsWhiteSpace(character) && character is not ':' and not '-')
                .Select(char.ToUpperInvariant)
                .ToArray()),
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
        Require(request.SerialNumber, nameof(request.SerialNumber), 64, errors);
        Require(request.Model, nameof(request.Model), 100, errors);
        Require(request.BootstrapCredentialId, nameof(request.BootstrapCredentialId), 64, errors);
        Require(request.FirmwareVersion, nameof(request.FirmwareVersion), 64, errors);
        Require(request.ConfigurationVersion, nameof(request.ConfigurationVersion), 64, errors);
        Require(factoryOperatorId?.Trim() ?? string.Empty, "FactoryOperatorId", 200, errors);

        if (request.SerialNumber.Length < 4
            || request.SerialNumber.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            errors.Add(new(nameof(request.SerialNumber),
                "Serial number must contain 4-64 letters, numbers, or hyphens."));
        }

        if (request.HardwareId.Length != 12 || request.HardwareId.Any(character => !Uri.IsHexDigit(character)))
        {
            errors.Add(new(nameof(request.HardwareId),
                "Hardware ID must be a 12-character ESP32 hexadecimal ID."));
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
            errors.Add(new(nameof(request.BootstrapSecretHash),
                "Bootstrap secret hash must be valid Base64."));
        }

        return errors;
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