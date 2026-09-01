using WaterFlex.SaltMonitor.Domain.Security;

namespace WaterFlex.SaltMonitor.Ingestion;

/// <summary>A WaterFlex customer surfaced to a technician while searching the commissioning directory.</summary>
public sealed record WaterFlexCustomerOption(
    string WaterFlexCustomerId,
    string AccountNumber,
    string DisplayName,
    IReadOnlyList<WaterFlexLocationOption> Locations);

/// <summary>A service location belonging to a <see cref="WaterFlexCustomerOption"/>.</summary>
public sealed record WaterFlexLocationOption(
    string WaterFlexLocationId,
    string DisplayName,
    string AddressSummary,
    IReadOnlyList<WaterFlexTankOption> Tanks);

/// <summary>A tank asset at a location that a sensor can be commissioned against.</summary>
public sealed record WaterFlexTankOption(
    string WaterFlexAssetId,
    string Label,
    int? CapacityPounds);

/// <summary>
/// The customer/location/tank combination a technician has picked from the WaterFlex directory,
/// flattened for use as the target of a commissioning request.
/// </summary>
public sealed record WaterFlexCommissioningSelection(
    string WaterFlexCustomerId,
    string AccountNumber,
    string CustomerDisplayName,
    string WaterFlexLocationId,
    string LocationDisplayName,
    string AddressSummary,
    string WaterFlexAssetId,
    string TankLabel,
    int? CapacityPounds);

/// <summary>
/// Read-only access to the WaterFlex customer/location/tank hierarchy used to steer a
/// technician toward the correct tank during commissioning.
/// </summary>
public interface IWaterFlexCustomerDirectory
{
    /// <summary>Searches customers (and their locations/tanks) by name or account number for the commissioning picker.</summary>
    Task<IReadOnlyList<WaterFlexCustomerOption>> SearchAsync(
        string? search,
        CancellationToken cancellationToken = default);

    /// <summary>Resolves a specific customer/location/tank triple, or null if the combination no longer exists in WaterFlex.</summary>
    Task<WaterFlexCommissioningSelection?> ResolveAsync(
        string waterFlexCustomerId,
        string waterFlexLocationId,
        string waterFlexAssetId,
        CancellationToken cancellationToken = default);
}

/// <summary>A technician's request to bind a physical sensor to a WaterFlex tank and begin monitoring it.</summary>
public sealed record CommissionSensorRequest(
    string WaterFlexCustomerId,
    string WaterFlexLocationId,
    string WaterFlexAssetId,
    string SerialNumber,
    string Model,
    string? WaterFlexWorkOrderId,
    decimal TankDepthCm,
    decimal CurrentDistanceCm);

/// <summary>Confirmation returned to the technician once a sensor has been commissioned, including its issued device token.</summary>
public sealed record CommissionSensorResponse(
    Guid DeviceId,
    Guid InstallationId,
    string SerialNumber,
    string DeviceToken,
    DateTimeOffset CommissionedAtUtc,
    string CustomerDisplayName,
    string LocationDisplayName,
    string AddressSummary,
    string TankLabel,
    int CalibrationVersion,
    decimal TankDepthCm,
    decimal CommissioningDistanceCm,
    double InitialFillPercent);

/// <summary>Reasons a commissioning attempt can fail, distinguishing input problems from state conflicts.</summary>
public enum CommissioningFailure
{
    None,
    InvalidRequest,
    DirectorySelectionNotFound,
    DeviceAlreadyRegistered,
    TankAlreadyOccupied,
    InvalidTechnician,
    Conflict
}

/// <summary>A single field-level validation failure for a commissioning request.</summary>
public sealed record CommissioningValidationError(
    string Field,
    string Message);

/// <summary>Outcome of a commissioning attempt, carrying either the new commissioning or the reason it was rejected.</summary>
public sealed record CommissioningResult(
    CommissionSensorResponse? Commissioning,
    CommissioningFailure Failure,
    IReadOnlyList<CommissioningValidationError> ValidationErrors)
{
    public bool IsSuccess => Failure == CommissioningFailure.None;

    public static CommissioningResult Success(CommissionSensorResponse commissioning) =>
        new(commissioning, CommissioningFailure.None, []);

    public static CommissioningResult Failed(
        CommissioningFailure failure,
        IReadOnlyList<CommissioningValidationError>? validationErrors = null) =>
        new(null, failure, validationErrors ?? []);
}

/// <summary>Binds a physical sensor to a WaterFlex tank, issuing calibration and a device token for subsequent telemetry.</summary>
public interface ISensorCommissioningService
{
    /// <summary>Commissions the sensor described by <paramref name="request"/> on behalf of the given technician.</summary>
    Task<CommissioningResult> CommissionAsync(
        CommissionSensorRequest request,
        StaffActor technician,
        CancellationToken cancellationToken = default);
}
