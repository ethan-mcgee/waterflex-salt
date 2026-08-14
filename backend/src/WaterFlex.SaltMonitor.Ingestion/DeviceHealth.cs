namespace WaterFlex.SaltMonitor.Ingestion;

public enum SensorHealthStatus
{
    Unknown,
    Healthy,
    Faulted
}

public enum SensorFaultCode
{
    ReadTimeout,
    InvalidSignal,
    OutOfRange,
    UnstableSignal,
    StuckHigh,
    StuckLow
}

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

public sealed record DeviceHealthAcknowledgement(
    DateTimeOffset ServerTimeUtc,
    int NextReportIntervalSeconds);

public enum DeviceHealthFailure
{
    None,
    InvalidPayload,
    DeviceUnavailable
}

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

public interface IDeviceHealthService
{
    Task<DeviceHealthResult> ReportAsync(
        Guid deviceId,
        DeviceHealthHeartbeat heartbeat,
        CancellationToken cancellationToken = default);
}
