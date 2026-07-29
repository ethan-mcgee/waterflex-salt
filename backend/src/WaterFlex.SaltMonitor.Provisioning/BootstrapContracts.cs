using WaterFlex.SaltMonitor.Domain.Security;

namespace WaterFlex.SaltMonitor.Provisioning;

public enum CommissioningSessionStatus
{
    PendingSensor,
    AwaitingFirstTelemetry,
    Completed,
    Expired,
    Cancelled,
    Failed
}

public sealed record RegisterFactoryDeviceRequest(
    string SerialNumber,
    string HardwareId,
    string Model,
    string BootstrapCredentialId,
    string BootstrapSecretHash,
    string FirmwareVersion,
    string ConfigurationVersion);

public sealed record FactoryDeviceRegistration(
    Guid DeviceId,
    string SerialNumber,
    string HardwareId,
    string Model,
    DateTimeOffset RegisteredAtUtc,
    string BootstrapCredentialId);

public enum FactoryRegistrationFailure
{
    None,
    InvalidRequest,
    DeviceAlreadyRegistered,
    BootstrapCredentialAlreadyRegistered,
    Conflict
}

public sealed record ProvisioningValidationError(string Field, string Message);

public sealed record FactoryRegistrationResult(
    FactoryDeviceRegistration? Registration,
    FactoryRegistrationFailure Failure,
    IReadOnlyList<ProvisioningValidationError> ValidationErrors)
{
    public bool IsSuccess => Failure == FactoryRegistrationFailure.None;

    public static FactoryRegistrationResult Success(FactoryDeviceRegistration registration) =>
        new(registration, FactoryRegistrationFailure.None, []);

    public static FactoryRegistrationResult Failed(
        FactoryRegistrationFailure failure,
        IReadOnlyList<ProvisioningValidationError>? validationErrors = null) =>
        new(null, failure, validationErrors ?? []);
}

public interface IFactoryDeviceRegistrationService
{
    Task<FactoryRegistrationResult> RegisterAsync(
        RegisterFactoryDeviceRequest request,
        string factoryOperatorId,
        CancellationToken cancellationToken = default);
}

public sealed record ActivateDeviceRequest(
    Guid ActivationAttemptId,
    string SerialNumber,
    string HardwareId,
    string FirmwareVersion,
    string ConfigurationVersion,
    string OperationalCredentialId,
    string OperationalSecretHash,
    int? CommissioningDistanceMm = null);

public sealed record ActivateDeviceResponse(
    Guid DeviceId,
    Guid InstallationId,
    string OperationalCredentialId,
    DateTimeOffset ActivatedAtUtc,
    string ActivationStatus);

public enum ActivationFailure
{
    None,
    InvalidRequest,
    InvalidBootstrapToken,
    BootstrapUnavailable,
    NoPendingCommissioning,
    ActivationConflict,
    ActivationAttemptMismatch,
    Conflict
}

public sealed record ActivationResult(
    ActivateDeviceResponse? Activation,
    ActivationFailure Failure,
    IReadOnlyList<ProvisioningValidationError> ValidationErrors)
{
    public bool IsSuccess => Failure == ActivationFailure.None;

    public static ActivationResult Success(ActivateDeviceResponse activation) =>
        new(activation, ActivationFailure.None, []);

    public static ActivationResult Failed(
        ActivationFailure failure,
        IReadOnlyList<ProvisioningValidationError>? validationErrors = null) =>
        new(null, failure, validationErrors ?? []);
}

public interface IDeviceBootstrapActivationService
{
    Task<ActivationResult> ActivateAsync(
        string bootstrapToken,
        ActivateDeviceRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record InstallationWorkOrderView(
    string WorkOrderNumber,
    string CustomerDisplayName,
    string LocationDisplayName,
    string AddressSummary,
    string? TankLocation);

public sealed record InstallationWorkOrder(
    string WorkOrderNumber,
    string DealerExternalId,
    string WaterFlexCustomerId,
    string WaterFlexLocationId,
    string WaterFlexAssetId,
    string CustomerDisplayName,
    string LocationDisplayName,
    string AddressSummary,
    string? TankLocation);

public interface IInstallationWorkOrderDirectory
{
    Task<InstallationWorkOrder?> FindEligibleAsync(
        string workOrderNumber,
        string dealerExternalId,
        CancellationToken cancellationToken = default);
}

public sealed record CreateWorkOrderCommissioningSessionRequest(
    string WorkOrderNumber,
    string SerialNumber,
    string? TankLocation,
    decimal TankDepthCm);

public sealed record CreateCommissioningSessionRequest(
    string WaterFlexCustomerId,
    string WaterFlexLocationId,
    string WaterFlexAssetId,
    string SerialNumber,
    string? WaterFlexWorkOrderId,
    decimal TankDepthCm);

public sealed record CommissioningSessionView(
    Guid SessionId,
    Guid DeviceId,
    string SerialNumber,
    CommissioningSessionStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string DealerName,
    string CustomerDisplayName,
    string LocationDisplayName,
    string AddressSummary,
    string TankLabel,
    decimal TankDepthCm,
    DateTimeOffset? ActivatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? FailureCode);

public enum CommissioningSessionFailure
{
    None,
    InvalidRequest,
    DirectorySelectionNotFound,
    DeviceNotFound,
    DeviceUnavailable,
    DeviceAlreadyReserved,
    TankUnavailable,
    SessionNotFound,
    SessionUnavailable,
    WorkOrderNotFound,
    TankLocationRequired,
    InvalidTechnician,
    Conflict
}

public sealed record CommissioningSessionResult(
    CommissioningSessionView? Session,
    CommissioningSessionFailure Failure,
    IReadOnlyList<ProvisioningValidationError> ValidationErrors)
{
    public bool IsSuccess => Failure == CommissioningSessionFailure.None;

    public static CommissioningSessionResult Success(CommissioningSessionView session) =>
        new(session, CommissioningSessionFailure.None, []);

    public static CommissioningSessionResult Failed(
        CommissioningSessionFailure failure,
        IReadOnlyList<ProvisioningValidationError>? validationErrors = null) =>
        new(null, failure, validationErrors ?? []);
}

public interface ICommissioningSessionService
{
    Task<InstallationWorkOrderView?> FindWorkOrderAsync(
        string workOrderNumber,
        StaffActor technician,
        CancellationToken cancellationToken = default);

    Task<CommissioningSessionResult> CreateFromWorkOrderAsync(
        CreateWorkOrderCommissioningSessionRequest request,
        StaffActor technician,
        CancellationToken cancellationToken = default);

    Task<CommissioningSessionResult> CreateAsync(
        CreateCommissioningSessionRequest request,
        StaffActor technician,
        CancellationToken cancellationToken = default);

    Task<CommissioningSessionResult> GetAsync(
        Guid sessionId,
        StaffActor technician,
        CancellationToken cancellationToken = default);

    Task<CommissioningSessionResult> CancelAsync(
        Guid sessionId,
        StaffActor technician,
        CancellationToken cancellationToken = default);
}