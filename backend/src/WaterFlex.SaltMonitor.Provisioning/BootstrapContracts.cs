using WaterFlex.SaltMonitor.Domain.Security;

namespace WaterFlex.SaltMonitor.Provisioning;

/// <summary>
/// Lifecycle of a technician-created commissioning session, from reserving a device for a
/// customer/tank through the device's own self-activation and first telemetry.
/// </summary>
public enum CommissioningSessionStatus
{
    PendingSensor,
    ActivatedAwaitingHealth,
    AwaitingFirstTelemetry,
    Completed,
    Expired,
    Cancelled,
    Failed
}

/// <summary>
/// Factory-floor request to pre-register a device before it ships, establishing the
/// bootstrap credential the sensor will later use to self-activate in the field.
/// </summary>
public sealed record RegisterFactoryDeviceRequest(
    string IdempotencyKey,
    string Model,
    string BootstrapCredentialId,
    string BootstrapSecretHash,
    string FirmwareVersion,
    string ConfigurationVersion);

/// <summary>Result of a successful factory registration, including the server-issued canonical serial number.</summary>
public sealed record FactoryDeviceRegistration(
    Guid DeviceId,
    string SerialNumber,
    string Model,
    DateTimeOffset RegisteredAtUtc,
    string BootstrapCredentialId);

/// <summary>Reasons a factory registration attempt was rejected.</summary>
public enum FactoryRegistrationFailure
{
    None,
    InvalidRequest,
    DeviceAlreadyRegistered,
    BootstrapCredentialAlreadyRegistered,
    Conflict
}

/// <summary>A single field-level validation failure surfaced to a provisioning caller.</summary>
public sealed record ProvisioningValidationError(string Field, string Message);

/// <summary>Outcome of a factory registration attempt, carrying either the registration or the reason it failed.</summary>
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

/// <summary>
/// Factory-floor registration and verification workflow, invoked by the factory provisioning
/// tool rather than by field technicians. Registration is keyed by idempotency key so a retried
/// factory run does not mint a second device or serial number.
/// </summary>
public interface IFactoryDeviceRegistrationService
{
    Task<FactoryRegistrationResult> RegisterAsync(
        RegisterFactoryDeviceRequest request,
        string factoryOperatorId,
        CancellationToken cancellationToken = default);

    Task<FactoryRegistrationResult> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        string factoryOperatorId,
        CancellationToken cancellationToken = default);

    Task<FactoryVerificationResult> RecordVerificationAsync(
        Guid deviceId,
        FactoryVerificationRequest request,
        string factoryOperatorId,
        CancellationToken cancellationToken = default);
}

/// <summary>End-of-line factory verification outcome for a device, gating whether it is allowed to ship.</summary>
public enum FactoryProvisioningStatus
{
    Registered,
    Provisioned,
    Failed,
    Quarantined
}

/// <summary>
/// Results of the end-of-line factory checks a device must pass before it is cleared to ship;
/// any false flag or a non-null failure code routes the device to <see cref="FactoryProvisioningStatus.Quarantined"/>.
/// </summary>
public sealed record FactoryVerificationRequest(
    bool FirmwareVerified,
    bool IdentityVerified,
    bool PortalVerified,
    bool SensorVerified,
    string FirmwareVersion,
    string? FailureCode);

/// <summary>Recorded outcome of a device's end-of-line factory verification.</summary>
public sealed record FactoryVerificationResult(
    Guid DeviceId,
    string SerialNumber,
    FactoryProvisioningStatus Status,
    DateTimeOffset VerifiedAtUtc,
    string? FailureCode);

/// <summary>
/// A sensor's own request to self-activate using its factory bootstrap credential, exchanging
/// the bootstrap token for an operational one. Idempotent per <see cref="ActivationAttemptId"/>
/// so a retried request (e.g. after a dropped Wi-Fi response) does not double-activate the device.
/// </summary>
public sealed record ActivateDeviceRequest(
    Guid ActivationAttemptId,
    string SerialNumber,
    string FirmwareVersion,
    string ConfigurationVersion,
    string OperationalCredentialId,
    string OperationalSecretHash,
    int? CommissioningDistanceMm = null);

/// <summary>Server response to a successful device self-activation, confirming the device's new operational identity.</summary>
public sealed record ActivateDeviceResponse(
    Guid DeviceId,
    Guid InstallationId,
    string OperationalCredentialId,
    DateTimeOffset ActivatedAtUtc,
    string ActivationStatus);

/// <summary>
/// Reasons a device's self-activation attempt was rejected. <see cref="NoPendingCommissioning"/>
/// means no technician has yet reserved the device via a commissioning session.
/// </summary>
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

/// <summary>Outcome of a device self-activation attempt.</summary>
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

/// <summary>
/// Exchanges a device's factory bootstrap token for its operational credential. This is the
/// no-technician-screen step of the bootstrap flow: the sensor calls this itself once it has
/// joined Wi-Fi, without any manual device-token entry.
/// </summary>
public interface IDeviceBootstrapActivationService
{
    Task<ActivationResult> ActivateAsync(
        string bootstrapToken,
        ActivateDeviceRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Technician-facing summary of a work order used to look one up before starting a commissioning session.</summary>
public sealed record InstallationWorkOrderView(
    string WorkOrderNumber,
    string CustomerDisplayName,
    string LocationDisplayName,
    string AddressSummary,
    string? TankLocation);

/// <summary>A dealer-sourced installation work order eligible to be turned into a commissioning session.</summary>
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

/// <summary>Looks up dealer-sourced work orders eligible to be commissioned, scoped to the requesting dealer.</summary>
public interface IInstallationWorkOrderDirectory
{
    Task<InstallationWorkOrder?> FindEligibleAsync(
        string workOrderNumber,
        string dealerExternalId,
        CancellationToken cancellationToken = default);
}

/// <summary>Starts a commissioning session from an existing dealer work order, carrying customer/location/asset identity implicitly.</summary>
public sealed record CreateWorkOrderCommissioningSessionRequest(
    string WorkOrderNumber,
    string SerialNumber,
    string? TankLocation,
    decimal TankDepthCm);

/// <summary>Starts a commissioning session without a work order, by supplying the WaterFlex customer/location/asset identifiers directly.</summary>
public sealed record CreateCommissioningSessionRequest(
    string WaterFlexCustomerId,
    string WaterFlexLocationId,
    string WaterFlexAssetId,
    string SerialNumber,
    string? WaterFlexWorkOrderId,
    decimal TankDepthCm);

/// <summary>
/// Technician-facing view of a commissioning session's progress, from device reservation
/// through self-activation to first telemetry.
/// </summary>
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

/// <summary>Reasons a commissioning session could not be created, retrieved, or cancelled.</summary>
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

/// <summary>Outcome of a commissioning session operation.</summary>
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

/// <summary>
/// Technician-driven half of the bootstrap flow: reserves a factory-registered device for a
/// customer/tank ahead of the device's own self-activation, and tracks that reservation through
/// to completion. Sessions expire (typically 30 minutes) if the device never checks in.
/// </summary>
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
