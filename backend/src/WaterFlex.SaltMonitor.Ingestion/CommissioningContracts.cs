using WaterFlex.SaltMonitor.Domain.Security;

namespace WaterFlex.SaltMonitor.Ingestion;

public sealed record WaterFlexCustomerOption(
    string WaterFlexCustomerId,
    string AccountNumber,
    string DisplayName,
    IReadOnlyList<WaterFlexLocationOption> Locations);

public sealed record WaterFlexLocationOption(
    string WaterFlexLocationId,
    string DisplayName,
    string AddressSummary,
    IReadOnlyList<WaterFlexTankOption> Tanks);

public sealed record WaterFlexTankOption(
    string WaterFlexAssetId,
    string Label,
    int? CapacityPounds);

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

public interface IWaterFlexCustomerDirectory
{
    Task<IReadOnlyList<WaterFlexCustomerOption>> SearchAsync(
        string? search,
        CancellationToken cancellationToken = default);

    Task<WaterFlexCommissioningSelection?> ResolveAsync(
        string waterFlexCustomerId,
        string waterFlexLocationId,
        string waterFlexAssetId,
        CancellationToken cancellationToken = default);
}

public sealed record CommissionSensorRequest(
    string WaterFlexCustomerId,
    string WaterFlexLocationId,
    string WaterFlexAssetId,
    string SerialNumber,
    string HardwareId,
    string Model,
    string? WaterFlexWorkOrderId,
    decimal TankDepthCm,
    decimal CurrentDistanceCm);

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

public sealed record CommissioningValidationError(
    string Field,
    string Message);

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

public interface ISensorCommissioningService
{
    Task<CommissioningResult> CommissionAsync(
        CommissionSensorRequest request,
        StaffActor technician,
        CancellationToken cancellationToken = default);
}