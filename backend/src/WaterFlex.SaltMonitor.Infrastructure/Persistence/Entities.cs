using WaterFlex.SaltMonitor.Provisioning;
using WaterFlex.SaltMonitor.Ingestion;
using WaterFlex.SaltMonitor.Domain.Security;
using WaterFlex.SaltMonitor.Domain.Monitoring;

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence;

/// <summary>Where a device sits in its life, from factory registration through field retirement.</summary>
public enum DeviceLifecycleStatus
{
    Registered,
    Commissioning,
    Active,
    Quarantined,
    Retired
}

/// <summary>An installing/servicing dealer organization, scoping which installations and staff belong to it.</summary>
public sealed class Dealer
{
    public Guid Id { get; set; }
    public required string ExternalId { get; set; }
    public required string DisplayName { get; set; }
    public bool IsActive { get; set; }
    public ICollection<DeviceInstallation> Installations { get; set; } = [];
}

/// <summary>A WaterFlex customer, mirrored locally from the WaterFlex system of record so provisioning and alerting can operate offline of it.</summary>
public sealed class CustomerAccount
{
    public Guid Id { get; set; }
    public required string WaterFlexCustomerId { get; set; }
    public string? AccountNumber { get; set; }
    public required string DisplayName { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset LastSyncedAtUtc { get; set; }
    public ICollection<ServiceLocation> ServiceLocations { get; set; } = [];
}

/// <summary>A physical site belonging to a <see cref="CustomerAccount"/> where one or more tanks may be installed.</summary>
public sealed class ServiceLocation
{
    public Guid Id { get; set; }
    public Guid CustomerAccountId { get; set; }
    public required string WaterFlexLocationId { get; set; }
    public required string DisplayName { get; set; }
    public string? AddressSummary { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset LastSyncedAtUtc { get; set; }
    public CustomerAccount CustomerAccount { get; set; } = null!;
    public ICollection<Tank> Tanks { get; set; } = [];
}

/// <summary>A physical salt tank at a <see cref="ServiceLocation"/>, tracked independently of whatever sensor is currently installed in it.</summary>
public sealed class Tank
{
    public Guid Id { get; set; }
    public Guid ServiceLocationId { get; set; }
    public string? WaterFlexAssetId { get; set; }
    public required string Label { get; set; }
    public int? CapacityPounds { get; set; }
    public bool IsActive { get; set; }
    public ServiceLocation ServiceLocation { get; set; } = null!;
    public ICollection<DeviceInstallation> Installations { get; set; } = [];
}

/// <summary>
/// A physical sensor unit, identified by its canonical serial number and tracked across its
/// entire lifecycle independent of any particular tank installation. Health-reporting fields
/// mirror the device's most recent self-reported status without requiring a join to telemetry.
/// </summary>
public sealed class Device
{
    public Guid Id { get; set; }
    public required string SerialNumber { get; set; }
    public required string Model { get; set; }
    public DeviceLifecycleStatus Status { get; set; }
    public DateTimeOffset RegisteredAtUtc { get; set; }
    public DateTimeOffset? CommissionedAtUtc { get; set; }
    public DateTimeOffset? RetiredAtUtc { get; set; }
    public string? FactoryFirmwareVersion { get; set; }
    public string? FactoryConfigurationVersion { get; set; }
    public string? FactoryProvisionedBy { get; set; }
    public DateTimeOffset? LastHealthReportedAtUtc { get; set; }
    public DateTimeOffset? LastDeviceReportedAtUtc { get; set; }
    public SensorHealthStatus LastSensorStatus { get; set; } = SensorHealthStatus.Unknown;
    public SensorFaultCode? LastSensorFault { get; set; }
    public string? LastHealthFirmwareVersion { get; set; }
    public int? LastHealthWifiRssiDbm { get; set; }
    public int LastQueuedReadingCount { get; set; }
    public int LastDroppedReadingCount { get; set; }
    public bool LastClockSynchronized { get; set; }
    public ICollection<DeviceCredential> Credentials { get; set; } = [];
    public ICollection<DeviceBootstrapCredential> BootstrapCredentials { get; set; } = [];
    public ICollection<DeviceInstallation> Installations { get; set; } = [];
    public ICollection<CommissioningSession> CommissioningSessions { get; set; } = [];
    public FactoryProvisioningJob? FactoryProvisioningJob { get; set; }
}

/// <summary>
/// Tracks a single factory registration attempt end-to-end, from the idempotency key the factory
/// tool submitted through to end-of-line verification. The idempotency key lets a retried factory
/// run recover the same job (and serial number) instead of creating a duplicate device.
/// </summary>
public sealed class FactoryProvisioningJob
{
    public Guid Id { get; set; }
    public required string IdempotencyKey { get; set; }
    public long SerialSequence { get; set; }
    public required string SerialNumber { get; set; }
    public FactoryProvisioningStatus Status { get; set; }
    public Guid DeviceId { get; set; }
    public required string CreatedBy { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? VerifiedAtUtc { get; set; }
    public string? FailureCode { get; set; }
    public uint RowVersion { get; set; }
    public Device Device { get; set; } = null!;
}

/// <summary>
/// The factory-issued credential a device uses to self-activate in the field. It is single-use:
/// once <see cref="ConsumedAtUtc"/> is set the credential can no longer activate a device.
/// <see cref="FailedAttemptCount"/> increments on a bad secret but is not yet checked against any
/// auto-revocation threshold.
/// </summary>
public sealed class DeviceBootstrapCredential
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public required string CredentialId { get; set; }
    public required byte[] SecretHash { get; set; }
    public DateTimeOffset ValidFromUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
    public DateTimeOffset? LastUsedAtUtc { get; set; }
    public int FailedAttemptCount { get; set; }
    public uint RowVersion { get; set; }
    public Device Device { get; set; } = null!;
}

/// <summary>
/// A short-lived, single-use credential the backend issues after a factory job is registered or
/// retried, which the local factory workstation helper must redeem before it is allowed to flash
/// the device. This is what closes the gap between the staff-authenticated backend and the
/// loopback-only helper process, which otherwise has no way to confirm the flash was authorized.
/// Minting a new authorization for a job revokes any prior unconsumed one for that job.
/// </summary>
public sealed class FactoryFlashAuthorization
{
    public Guid Id { get; set; }
    public Guid FactoryProvisioningJobId { get; set; }
    public Guid DeviceId { get; set; }
    public required string CredentialId { get; set; }
    public required byte[] SecretHash { get; set; }
    public DateTimeOffset IssuedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
    public Guid? RedeemedByFactoryStationId { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public int FailedAttemptCount { get; set; }
    public uint RowVersion { get; set; }
    public FactoryProvisioningJob Job { get; set; } = null!;
}

/// <summary>
/// Short-lived credential issued only when the local helper redeems an approved flash token.
/// It binds end-of-line evidence to the exact device and release that were authorized to flash.
/// </summary>
public sealed class FactoryVerificationAuthorization
{
    public Guid Id { get; set; }
    public Guid FactoryProvisioningJobId { get; set; }
    public Guid DeviceId { get; set; }
    public Guid FactoryStationId { get; set; }
    public required string CredentialId { get; set; }
    public required byte[] SecretHash { get; set; }
    public required string FirmwareVersion { get; set; }
    public required string ConfigurationVersion { get; set; }
    public required string BundleSha256 { get; set; }
    public DateTimeOffset IssuedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public string? ResultJson { get; set; }
    public uint RowVersion { get; set; }
    public FactoryProvisioningJob Job { get; set; } = null!;
}

public sealed class FactoryStation
{
    public Guid Id { get; set; }
    public required string DisplayName { get; set; }
    public required string PublicKey { get; set; }
    public required string Thumbprint { get; set; }
    public required string KeyProviderType { get; set; }
    public required string HelperVersion { get; set; }
    public required string ProtocolVersion { get; set; }
    public DateTimeOffset EnrolledAtUtc { get; set; }
    public DateTimeOffset? LastSeenAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public uint RowVersion { get; set; }
}

public sealed class FactoryStationEnrollmentGrant
{
    public Guid Id { get; set; }
    public required byte[] SecretHash { get; set; }
    public required string DisplayName { get; set; }
    public required string PublicKey { get; set; }
    public required string Thumbprint { get; set; }
    public required string CreatedBy { get; set; }
    public DateTimeOffset IssuedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
    public uint RowVersion { get; set; }
}

public sealed class FactoryStationReplayNonce
{
    public Guid Id { get; set; }
    public Guid FactoryStationId { get; set; }
    public required string Nonce { get; set; }
    public DateTimeOffset UsedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public FactoryStation Station { get; set; } = null!;
}

/// <summary>The operational credential a device uses for ongoing telemetry/API calls after it has been activated or directly commissioned.</summary>
public sealed class DeviceCredential
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public required string CredentialId { get; set; }
    public required byte[] SecretHash { get; set; }
    public DateTimeOffset ValidFromUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public DateTimeOffset? LastUsedAtUtc { get; set; }
    public Device Device { get; set; } = null!;
}

/// <summary>
/// A single physical placement of a <see cref="Device"/> into a <see cref="Tank"/>. Kept distinct
/// from the device and tank themselves so a sensor's installation history (swap, removal,
/// reinstall elsewhere) can be tracked without losing prior calibration or telemetry.
/// </summary>
public sealed class DeviceInstallation
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public Guid TankId { get; set; }
    public Guid? DealerId { get; set; }
    public DateTimeOffset InstalledAtUtc { get; set; }
    public DateTimeOffset? RemovedAtUtc { get; set; }
    public string? InstalledBy { get; set; }
    public string? WaterFlexWorkOrderId { get; set; }
    public uint RowVersion { get; set; }
    public Device Device { get; set; } = null!;
    public Tank Tank { get; set; } = null!;
    public Dealer? Dealer { get; set; }
    public ICollection<TankCalibrationRecord> Calibrations { get; set; } = [];
}

/// <summary>
/// A versioned calibration for a specific <see cref="DeviceInstallation"/>. Recalibrating an
/// installed sensor creates a new version rather than overwriting the prior one, so historical
/// telemetry keeps the calibration that was actually in effect when it was recorded.
/// </summary>
public sealed class TankCalibrationRecord
{
    public Guid Id { get; set; }
    public Guid DeviceInstallationId { get; set; }
    public int Version { get; set; }
    public int TankDepthMm { get; set; }
    public int CommissioningDistanceMm { get; set; }
    public DateTimeOffset EffectiveFromUtc { get; set; }
    public DateTimeOffset? EffectiveToUtc { get; set; }
    public required string CreatedBy { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DeviceInstallation DeviceInstallation { get; set; } = null!;
}

/// <summary>
/// A single fill-level reading ingested from a sensor. <see cref="BootId"/> and
/// <see cref="SequenceNumber"/> together let ingestion detect gaps or reordering across a
/// device's power cycles.
/// </summary>
public sealed class TelemetryReadingRecord
{
    public long Id { get; set; }
    public Guid DeviceId { get; set; }
    public Guid DeviceInstallationId { get; set; }
    public Guid TankCalibrationRecordId { get; set; }
    public Guid BootId { get; set; }
    public long SequenceNumber { get; set; }
    public DateTimeOffset? ObservedAtUtc { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public long UptimeMilliseconds { get; set; }
    public int RawDistanceMm { get; set; }
    public double FillPercent { get; set; }
    public int Quality { get; set; }
    public int SampleCount { get; set; }
    public int WifiRssiDbm { get; set; }
    public required string FirmwareVersion { get; set; }
    public required string ErrorFlagsJson { get; set; }
    public Device Device { get; set; } = null!;
    public DeviceInstallation DeviceInstallation { get; set; } = null!;
    public TankCalibrationRecord TankCalibrationRecord { get; set; } = null!;
}

/// <summary>
/// A technician's in-progress reservation of a factory-registered device for a customer/tank,
/// pending the device's own self-activation. Expires (see <see cref="ExpiresAtUtc"/>) if the
/// device never checks in.
/// </summary>
public sealed class CommissioningSession
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public Guid DealerId { get; set; }
    public Guid TankId { get; set; }
    public Guid? ProvisionalCredentialId { get; set; }
    public CommissioningSessionStatus Status { get; set; }
    public int TankDepthMm { get; set; }
    public string? WaterFlexWorkOrderId { get; set; }
    public required string CreatedByActorId { get; set; }
    public required string CreatedByDisplayName { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? ActivatedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
    public Guid? ActivationAttemptId { get; set; }
    public string? FailureCode { get; set; }
    public uint RowVersion { get; set; }
    public Device Device { get; set; } = null!;
    public Dealer Dealer { get; set; } = null!;
    public Tank Tank { get; set; } = null!;
    public DeviceCredential? ProvisionalCredential { get; set; }
    public ICollection<ProvisioningAuditEvent> AuditEvents { get; set; } = [];
}

/// <summary>Append-only audit trail of provisioning-related events (registration, commissioning, activation) for compliance and troubleshooting.</summary>
public sealed class ProvisioningAuditEvent
{
    public long Id { get; set; }
    public Guid? DeviceId { get; set; }
    public Guid? CommissioningSessionId { get; set; }
    public required string EventType { get; set; }
    public required string ActorType { get; set; }
    public required string ActorId { get; set; }
    public required string DetailsJson { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public Device? Device { get; set; }
    public CommissioningSession? CommissioningSession { get; set; }
}

/// <summary>A staff member's identity as mapped from their external identity-provider login (e.g. Cognito) to their role and dealer scope in this system.</summary>
public sealed class StaffIdentityRecord
{
    public Guid Id { get; set; }
    public required string Issuer { get; set; }
    public required string Subject { get; set; }
    public required string Email { get; set; }
    public string NormalizedEmail { get; set; } = string.Empty;
    public required string DisplayName { get; set; }
    public StaffRole Role { get; set; }
    public Guid? DealerId { get; set; }
    public bool IsActive { get; set; }
    public StaffIdentityState State { get; set; }
    public string? CognitoUsername { get; set; }
    public DateTimeOffset? ActivatedAtUtc { get; set; }
    public DateTimeOffset? SuspendedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public uint RowVersion { get; set; }
    public Dealer? Dealer { get; set; }
}

/// <summary>A pending invitation for a new staff member, resolved into a <see cref="StaffIdentityRecord"/> once they accept and their identity-provider login is linked.</summary>
public sealed class StaffInvitation
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public required string NormalizedEmail { get; set; }
    public required string DisplayName { get; set; }
    public StaffRole Role { get; set; }
    public Guid? DealerId { get; set; }
    public StaffInvitationStatus Status { get; set; }
    public required string CreatedByStaffId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? AcceptedAtUtc { get; set; }
    public Guid? AcceptedStaffIdentityId { get; set; }
    public string? FailureReason { get; set; }
    public uint RowVersion { get; set; }
    public Dealer? Dealer { get; set; }
    public StaffIdentityRecord? AcceptedStaffIdentity { get; set; }
}

/// <summary>Append-only audit trail of staff access changes (invitations, role changes, suspensions) for compliance and troubleshooting.</summary>
public sealed class StaffAccessAuditEvent
{
    public long Id { get; set; }
    public required string EventType { get; set; }
    public required string ActorStaffId { get; set; }
    public Guid? TargetStaffIdentityId { get; set; }
    public Guid? InvitationId { get; set; }
    public required string Reason { get; set; }
    public required string DetailsJson { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}

/// <summary>Outbox queue for asynchronous staff-provisioning side effects (e.g. identity-provider account creation) that must survive process restarts and be retried on failure.</summary>
public sealed class StaffProvisioningWorkItem
{
    public long Id { get; set; }
    public required string WorkType { get; set; }
    public StaffProvisioningWorkStatus Status { get; set; }
    public Guid? StaffIdentityId { get; set; }
    public Guid? InvitationId { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string PayloadJson { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset AvailableAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? LastError { get; set; }
}

/// <summary>Hourly rollup of a device's telemetry, maintained so dashboards and history views don't have to scan raw readings for anything beyond the most recent window.</summary>
public sealed class TelemetryHourlySummary
{
    public Guid DeviceId { get; set; }
    public DateTimeOffset BucketStartUtc { get; set; }
    public DateTimeOffset LastReadingAtUtc { get; set; }
    public long ReadingCount { get; set; }
    public double FillPercentMin { get; set; }
    public double FillPercentMax { get; set; }
    public double FillPercentAverage { get; set; }
    public double FillPercentLatest { get; set; }
    public int RawDistanceMmMin { get; set; }
    public int RawDistanceMmMax { get; set; }
    public double RawDistanceMmAverage { get; set; }
    public int WifiRssiDbmMin { get; set; }
    public int WifiRssiDbmMax { get; set; }
    public double WifiRssiDbmAverage { get; set; }
    public int WorstQuality { get; set; }
    public long ErrorCount { get; set; }
    public required string LatestFirmwareVersion { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public Device Device { get; set; } = null!;
}

/// <summary>Daily rollup of a device's telemetry, retained far longer than raw readings once <see cref="TelemetryHistoryMaintenanceService"/> prunes them.</summary>
public sealed class TelemetryDailySummary
{
    public Guid DeviceId { get; set; }
    public DateTimeOffset BucketStartUtc { get; set; }
    public DateTimeOffset LastReadingAtUtc { get; set; }
    public long ReadingCount { get; set; }
    public double FillPercentMin { get; set; }
    public double FillPercentMax { get; set; }
    public double FillPercentAverage { get; set; }
    public double FillPercentLatest { get; set; }
    public int RawDistanceMmMin { get; set; }
    public int RawDistanceMmMax { get; set; }
    public double RawDistanceMmAverage { get; set; }
    public int WifiRssiDbmMin { get; set; }
    public int WifiRssiDbmMax { get; set; }
    public double WifiRssiDbmAverage { get; set; }
    public int WorstQuality { get; set; }
    public long ErrorCount { get; set; }
    public required string LatestFirmwareVersion { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public Device Device { get; set; } = null!;
}

/// <summary>Tracks the last completion time of a named background maintenance task (e.g. telemetry history pruning) so it runs at most once per its interval across process restarts.</summary>
public sealed class TelemetryMaintenanceState
{
    public required string Name { get; set; }
    public DateTimeOffset CompletedAtUtc { get; set; }
}

/// <summary>
/// A low-salt alert for a tank installation, open from the moment debounced low-fill evidence
/// first appears until it is resolved, acknowledged/approved by staff, or dismissed. Drives
/// automatic delivery-ticket creation (see <see cref="DeliveryTicket"/>).
/// </summary>
public sealed class LowSaltAlert
{
    public Guid Id { get; set; }
    public Guid DeviceInstallationId { get; set; }
    public LowSaltAlertStatus Status { get; set; }
    public DateTimeOffset OpenedAtUtc { get; set; }
    public DateTimeOffset LastEvidenceAtUtc { get; set; }
    public double LastEvidenceFillPercent { get; set; }
    public DateTimeOffset? AcknowledgedAtUtc { get; set; }
    public string? AcknowledgedBy { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? DismissedAtUtc { get; set; }
    public string? DismissedBy { get; set; }
    public string? DismissalReason { get; set; }
    public DateTimeOffset? ResolvedAtUtc { get; set; }
    public uint RowVersion { get; set; }
    public DeviceInstallation DeviceInstallation { get; set; } = null!;
    public ICollection<LowSaltAlertAuditEvent> AuditEvents { get; set; } = [];
}

/// <summary>Append-only audit trail of a <see cref="LowSaltAlert"/>'s lifecycle transitions, for staff review and dispute resolution.</summary>
public sealed class LowSaltAlertAuditEvent
{
    public long Id { get; set; }
    public Guid LowSaltAlertId { get; set; }
    public required string EventType { get; set; }
    public required string ActorType { get; set; }
    public required string ActorId { get; set; }
    public string? Reason { get; set; }
    public long? TelemetryReadingId { get; set; }
    public double? FillPercent { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public LowSaltAlert LowSaltAlert { get; set; } = null!;
}

/// <summary>
/// Per-installation running counters for the low-salt debounce/recovery/suppression rules,
/// persisted so evaluation survives process restarts instead of re-deriving state from raw
/// telemetry history on every run.
/// </summary>
public sealed class LowSaltAlertEvaluationState
{
    public Guid DeviceInstallationId { get; set; }
    public int BelowEvidenceCount { get; set; }
    public DateTimeOffset? FirstBelowEvidenceAtUtc { get; set; }
    public int RecoveryEvidenceCount { get; set; }
    public DateTimeOffset? FirstRecoveryEvidenceAtUtc { get; set; }
    public DateTimeOffset? SuppressedUntilUtc { get; set; }
    public long? LastProcessedTelemetryReadingId { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DeviceInstallation DeviceInstallation { get; set; } = null!;
}

/// <summary>Processing state of a queued outbox work item, shared by the alert and delivery-ticket work queues.</summary>
public enum AlertWorkItemStatus
{
    Pending,
    Processing,
    Completed,
    DeadLetter
}

/// <summary>Outbox queue driving low-salt alert evaluation for a newly ingested <see cref="TelemetryReadingRecord"/>, decoupling ingestion from evaluation so a slow or failing evaluation never blocks telemetry writes.</summary>
public sealed class AlertEvaluationWorkItem
{
    public long Id { get; set; }
    public long TelemetryReadingId { get; set; }
    public AlertWorkItemStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset AvailableAtUtc { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeasedUntilUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? LastError { get; set; }
    public TelemetryReadingRecord TelemetryReading { get; set; } = null!;
}

/// <summary>
/// One delivery ticket per <see cref="LowSaltAlert"/>, opened automatically when the alert
/// opens and resolved automatically when the alert resolves from sensor recovery evidence.
/// </summary>
public sealed class DeliveryTicket
{
    public Guid Id { get; set; }
    public Guid LowSaltAlertId { get; set; }
    public DeliveryTicketStatus Status { get; set; }
    public string? ExternalTicketId { get; set; }
    public required string IdempotencyKey { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ExternalCreatedAtUtc { get; set; }
    public DateTimeOffset? ResolvedAtUtc { get; set; }
    public string? LastError { get; set; }
    public LowSaltAlert LowSaltAlert { get; set; } = null!;
}

/// <summary>Outbox queue driving the actual <see cref="WaterFlex.SaltMonitor.Domain.Abstractions.IDeliveryTicketGateway"/> call for a <see cref="DeliveryTicket"/>.</summary>
public sealed class DeliveryTicketWorkItem
{
    public long Id { get; set; }
    public Guid DeliveryTicketId { get; set; }
    public AlertWorkItemStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset AvailableAtUtc { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeasedUntilUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? LastError { get; set; }
    public DeliveryTicket DeliveryTicket { get; set; } = null!;
}
