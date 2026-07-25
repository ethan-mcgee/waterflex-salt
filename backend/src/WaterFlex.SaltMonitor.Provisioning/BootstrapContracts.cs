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