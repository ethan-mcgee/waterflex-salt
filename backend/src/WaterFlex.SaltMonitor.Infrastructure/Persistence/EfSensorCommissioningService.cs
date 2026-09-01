using System.Data;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WaterFlex.SaltMonitor.Domain.Level;
using WaterFlex.SaltMonitor.Domain.Model;
using WaterFlex.SaltMonitor.Domain.Security;
using WaterFlex.SaltMonitor.Ingestion;

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence;

/// <summary>
/// EF-backed implementation of the direct commissioning path: a technician hand-types a sensor's
/// serial number and tank calibration and this issues its operational credential immediately, with
/// no factory pre-registration or bootstrap handshake involved. This is the older of the two
/// provisioning flows (see <see cref="WaterFlex.SaltMonitor.Provisioning.ICommissioningSessionService"/>
/// for the newer factory bootstrap/self-activation flow) and mints a device token that is returned once for the
/// technician to paste into the sensor's captive Wi-Fi portal.
/// </summary>
public sealed class EfSensorCommissioningService(
    SaltMonitorDbContext dbContext,
    IWaterFlexCustomerDirectory customerDirectory,
    TimeProvider timeProvider) : ISensorCommissioningService
{
    public async Task<CommissioningResult> CommissionAsync(
        CommissionSensorRequest request,
        StaffActor technician,
        CancellationToken cancellationToken = default)
    {
        if (technician.Role != StaffRole.DealerTechnician
            || string.IsNullOrWhiteSpace(technician.DealerExternalId)
            || string.IsNullOrWhiteSpace(technician.DealerName))
        {
            return CommissioningResult.Failed(CommissioningFailure.InvalidTechnician);
        }

        var normalizedRequest = Normalize(request);
        var validationErrors = Validate(normalizedRequest);
        if (validationErrors.Count > 0)
        {
            return CommissioningResult.Failed(
                CommissioningFailure.InvalidRequest,
                validationErrors);
        }

        var selection = await customerDirectory.ResolveAsync(
            normalizedRequest.WaterFlexCustomerId,
            normalizedRequest.WaterFlexLocationId,
            normalizedRequest.WaterFlexAssetId,
            cancellationToken);
        if (selection is null)
        {
            return CommissioningResult.Failed(CommissioningFailure.DirectorySelectionNotFound);
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(() =>
                CommissionWithinTransactionAsync(
                    normalizedRequest,
                    selection,
                    technician,
                    cancellationToken));
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            return CommissioningResult.Failed(CommissioningFailure.Conflict);
        }
    }

    private async Task<CommissioningResult> CommissionWithinTransactionAsync(
        CommissionSensorRequest request,
        WaterFlexCommissioningSelection selection,
        StaffActor technician,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var deviceExists = await dbContext.Devices.AnyAsync(
            device => device.SerialNumber == request.SerialNumber,
            cancellationToken);
        if (deviceExists)
        {
            return CommissioningResult.Failed(CommissioningFailure.DeviceAlreadyRegistered);
        }

        var now = timeProvider.GetUtcNow();
        var dealer = await dbContext.Dealers.SingleOrDefaultAsync(
            candidate => candidate.ExternalId == technician.DealerExternalId,
            cancellationToken);
        if (dealer is null)
        {
            dealer = new Dealer
            {
                Id = Guid.NewGuid(),
                ExternalId = technician.DealerExternalId!,
                DisplayName = technician.DealerName!,
                IsActive = true
            };
            dbContext.Dealers.Add(dealer);
        }
        else
        {
            dealer.DisplayName = technician.DealerName!;
            dealer.IsActive = true;
        }

        var customer = await dbContext.CustomerAccounts.SingleOrDefaultAsync(
            candidate => candidate.WaterFlexCustomerId == selection.WaterFlexCustomerId,
            cancellationToken);
        if (customer is null)
        {
            customer = new CustomerAccount
            {
                Id = Guid.NewGuid(),
                WaterFlexCustomerId = selection.WaterFlexCustomerId,
                AccountNumber = selection.AccountNumber,
                DisplayName = selection.CustomerDisplayName,
                IsActive = true,
                LastSyncedAtUtc = now
            };
            dbContext.CustomerAccounts.Add(customer);
        }
        else
        {
            customer.AccountNumber = selection.AccountNumber;
            customer.DisplayName = selection.CustomerDisplayName;
            customer.IsActive = true;
            customer.LastSyncedAtUtc = now;
        }

        var location = await dbContext.ServiceLocations.SingleOrDefaultAsync(
            candidate => candidate.CustomerAccountId == customer.Id
                && candidate.WaterFlexLocationId == selection.WaterFlexLocationId,
            cancellationToken);
        if (location is null)
        {
            location = new ServiceLocation
            {
                Id = Guid.NewGuid(),
                CustomerAccountId = customer.Id,
                WaterFlexLocationId = selection.WaterFlexLocationId,
                DisplayName = selection.LocationDisplayName,
                AddressSummary = selection.AddressSummary,
                IsActive = true,
                LastSyncedAtUtc = now
            };
            dbContext.ServiceLocations.Add(location);
        }
        else
        {
            location.DisplayName = selection.LocationDisplayName;
            location.AddressSummary = selection.AddressSummary;
            location.IsActive = true;
            location.LastSyncedAtUtc = now;
        }

        var tank = await dbContext.Tanks.SingleOrDefaultAsync(
            candidate => candidate.ServiceLocationId == location.Id
                && candidate.WaterFlexAssetId == selection.WaterFlexAssetId,
            cancellationToken);
        if (tank is null)
        {
            tank = new Tank
            {
                Id = Guid.NewGuid(),
                ServiceLocationId = location.Id,
                WaterFlexAssetId = selection.WaterFlexAssetId,
                Label = selection.TankLabel,
                CapacityPounds = selection.CapacityPounds,
                IsActive = true
            };
            dbContext.Tanks.Add(tank);
        }
        else
        {
            var tankOccupied = await dbContext.DeviceInstallations.AnyAsync(
                installation => installation.TankId == tank.Id
                    && installation.RemovedAtUtc == null,
                cancellationToken);
            if (tankOccupied)
            {
                return CommissioningResult.Failed(CommissioningFailure.TankAlreadyOccupied);
            }

            tank.Label = selection.TankLabel;
            tank.CapacityPounds = selection.CapacityPounds;
            tank.IsActive = true;
        }

        var device = new Device
        {
            Id = Guid.NewGuid(),
            SerialNumber = request.SerialNumber,
            Model = request.Model,
            Status = DeviceLifecycleStatus.Active,
            RegisteredAtUtc = now,
            CommissionedAtUtc = now
        };
        var installation = new DeviceInstallation
        {
            Id = Guid.NewGuid(),
            DeviceId = device.Id,
            TankId = tank.Id,
            DealerId = dealer.Id,
            InstalledAtUtc = now,
            InstalledBy = technician.DisplayName,
            WaterFlexWorkOrderId = request.WaterFlexWorkOrderId
        };
        var tankDepthMm = CentimetersToMillimeters(request.TankDepthCm);
        var commissioningDistanceMm = CentimetersToMillimeters(request.CurrentDistanceCm);
        var calibration = new TankCalibrationRecord
        {
            Id = Guid.NewGuid(),
            DeviceInstallationId = installation.Id,
            Version = 1,
            TankDepthMm = tankDepthMm,
            CommissioningDistanceMm = commissioningDistanceMm,
            EffectiveFromUtc = now,
            CreatedBy = technician.DisplayName,
            CreatedAtUtc = now
        };
        var credentialId = $"wf_{Base64UrlEncode(RandomNumberGenerator.GetBytes(12))}";
        var secret = RandomNumberGenerator.GetBytes(32);
        var credential = new DeviceCredential
        {
            Id = Guid.NewGuid(),
            DeviceId = device.Id,
            CredentialId = credentialId,
            SecretHash = SHA256.HashData(secret),
            ValidFromUtc = now
        };

        dbContext.AddRange(device, installation, calibration, credential);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var initialFillPercent = FillCalculator.CalculateFillPercent(
            commissioningDistanceMm,
            new TankCalibration(tankDepthMm));

        return CommissioningResult.Success(new(
            device.Id,
            installation.Id,
            device.SerialNumber,
            $"{credentialId}.{Base64UrlEncode(secret)}",
            now,
            selection.CustomerDisplayName,
            selection.LocationDisplayName,
            selection.AddressSummary,
            selection.TankLabel,
            calibration.Version,
            MillimetersToCentimeters(calibration.TankDepthMm),
            MillimetersToCentimeters(calibration.CommissioningDistanceMm),
            initialFillPercent));
    }

    private static CommissionSensorRequest Normalize(CommissionSensorRequest request) =>
        request with
        {
            WaterFlexCustomerId = request.WaterFlexCustomerId.Trim(),
            WaterFlexLocationId = request.WaterFlexLocationId.Trim(),
            WaterFlexAssetId = request.WaterFlexAssetId.Trim(),
            SerialNumber = request.SerialNumber.Trim().ToUpperInvariant(),
            Model = request.Model.Trim(),
            WaterFlexWorkOrderId = string.IsNullOrWhiteSpace(request.WaterFlexWorkOrderId)
                ? null
                : request.WaterFlexWorkOrderId.Trim()
        };

    private static IReadOnlyList<CommissioningValidationError> Validate(CommissionSensorRequest request)
    {
        var errors = new List<CommissioningValidationError>();
        Require(request.WaterFlexCustomerId, nameof(request.WaterFlexCustomerId), 128, errors);
        Require(request.WaterFlexLocationId, nameof(request.WaterFlexLocationId), 128, errors);
        Require(request.WaterFlexAssetId, nameof(request.WaterFlexAssetId), 128, errors);
        Require(request.SerialNumber, nameof(request.SerialNumber), 64, errors);
        Require(request.Model, nameof(request.Model), 100, errors);

        if (request.SerialNumber.Length < 4
            || request.SerialNumber.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            errors.Add(new(nameof(request.SerialNumber), "Serial number must contain 4-64 letters, numbers, or hyphens."));
        }

        if (request.WaterFlexWorkOrderId?.Length > 128)
        {
            errors.Add(new(nameof(request.WaterFlexWorkOrderId), "Work order ID cannot exceed 128 characters."));
        }

        if (request.TankDepthCm is < 10m or > 450m)
        {
            errors.Add(new(nameof(request.TankDepthCm), "Tank depth must be between 10 and 450 centimeters."));
        }

        if (request.CurrentDistanceCm is < 3m or > 450m)
        {
            errors.Add(new(nameof(request.CurrentDistanceCm), "Current sensor distance must be between 3 and 450 centimeters."));
        }

        if (request.CurrentDistanceCm > request.TankDepthCm)
        {
            errors.Add(new(
                nameof(request.CurrentDistanceCm),
                "Current sensor distance cannot be greater than tank depth."));
        }

        if (!HasMillimeterPrecision(request.TankDepthCm))
        {
            errors.Add(new(nameof(request.TankDepthCm), "Tank depth supports one decimal place in centimeters."));
        }

        if (!HasMillimeterPrecision(request.CurrentDistanceCm))
        {
            errors.Add(new(nameof(request.CurrentDistanceCm), "Current sensor distance supports one decimal place in centimeters."));
        }

        return errors;
    }

    private static void Require(
        string value,
        string field,
        int maximumLength,
        ICollection<CommissioningValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
        {
            errors.Add(new(field, $"{field} is required and cannot exceed {maximumLength} characters."));
        }
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool HasMillimeterPrecision(decimal centimeters) =>
        decimal.Truncate(centimeters * 10m) == centimeters * 10m;

    private static int CentimetersToMillimeters(decimal centimeters) =>
        decimal.ToInt32(decimal.Round(centimeters * 10m, 0, MidpointRounding.AwayFromZero));

    private static decimal MillimetersToCentimeters(int millimeters) => millimeters / 10m;

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: "23505" };
}
