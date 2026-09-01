namespace WaterFlex.SaltMonitor.Ingestion;

/// <summary>Self-reported operating condition of a sensor's ranging hardware, as of its last heartbeat.</summary>
public enum SensorHealthStatus
{
    Unknown,
    Healthy,
    Faulted
}

/// <summary>Specific reason a sensor reports itself as faulted, used to distinguish device problems from ordinary low-quality readings.</summary>
public enum SensorFaultCode
{
    ReadTimeout,
    InvalidSignal,
    OutOfRange,
    UnstableSignal
}

/// <summary>Periodic out-of-band status report a device sends alongside (or instead of) telemetry, used for fleet health monitoring rather than fill readings.</summary>
public sealed record DeviceHealthHeartbeat(
    int SchemaVersion,
    string FirmwareVersion,
    DateTimeOffset? ReportedAtUtc,
    long UptimeMilliseconds,
    SensorHealthStatus SensorStatus,
    SensorFaultCode? SensorFault,
    int WifiRssiDbm,
    int QueuedReadingCount,
    bool ClockSynchronized,
    int DroppedReadingCount = 0);

/// <summary>Server response to a heartbeat, telling the device when to check in again.</summary>
public sealed record DeviceHealthAcknowledgement(
    DateTimeOffset ServerTimeUtc,
    int NextReportIntervalSeconds);

/// <summary>Reasons a device health report can be rejected.</summary>
public enum DeviceHealthFailure
{
    None,
    InvalidPayload,
    DeviceUnavailable
}

/// <summary>Outcome of processing a device health heartbeat.</summary>
public sealed record DeviceHealthResult(
    DeviceHealthAcknowledgement? Acknowledgement,
    DeviceHealthFailure Failure,
    IReadOnlyList<TelemetryValidationError> ValidationErrors)
{
    public bool IsSuccess => Failure == DeviceHealthFailure.None;

    public static DeviceHealthResult Success(DeviceHealthAcknowledgement acknowledgement) =>
        new(acknowledgement, DeviceHealthFailure.None, []);

    public static DeviceHealthResult Failed(
        DeviceHealthFailure failure,
        IReadOnlyList<TelemetryValidationError>? validationErrors = null) =>
        new(null, failure, validationErrors ?? []);
}

/// <summary>Records device health heartbeats so fleet monitoring can distinguish a quiet device from a faulted one.</summary>
public interface IDeviceHealthService
{
    /// <summary>Accepts a heartbeat from the given device and returns the next check-in schedule.</summary>
    Task<DeviceHealthResult> ReportAsync(
        Guid deviceId,
        DeviceHealthHeartbeat heartbeat,
        CancellationToken cancellationToken = default);
}
