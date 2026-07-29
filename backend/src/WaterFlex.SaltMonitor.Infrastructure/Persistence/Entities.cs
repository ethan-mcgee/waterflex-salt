using WaterFlex.SaltMonitor.Provisioning;

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