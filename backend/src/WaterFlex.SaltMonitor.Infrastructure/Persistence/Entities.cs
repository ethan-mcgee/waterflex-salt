using WaterFlex.SaltMonitor.Provisioning;
using WaterFlex.SaltMonitor.Ingestion;
using WaterFlex.SaltMonitor.Domain.Security;
using WaterFlex.SaltMonitor.Domain.Monitoring;

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence;

public enum DeviceLifecycleStatus
{
    Registered,
    Commissioning,
    Active,
    Retired
}

public sealed class Dealer
{
    public Guid Id { get; set; }
    public required string ExternalId { get; set; }
    public required string DisplayName { get; set; }
    public bool IsActive { get; set; }
    public ICollection<DeviceInstallation> Installations { get; set; } = [];
}

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

public sealed class Device
{
    public Guid Id { get; set; }
    public required string SerialNumber { get; set; }
    public required string HardwareId { get; set; }
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
}

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

public sealed class TelemetryMaintenanceState
{
    public required string Name { get; set; }
    public DateTimeOffset CompletedAtUtc { get; set; }
}

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

public enum AlertWorkItemStatus
{
    Pending,
    Processing,
    Completed,
    DeadLetter
}

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

/// <summary>Outbox queue driving the actual <see cref="IDeliveryTicketGateway"/> call for a <see cref="DeliveryTicket"/>.</summary>
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
