namespace WaterFlex.SaltMonitor.Ingestion;

/// <summary>A batch of ranging readings a device uploads in a single request, rather than streaming one at a time.</summary>
public sealed record TelemetryBatch(
    int SchemaVersion,
    string FirmwareVersion,
    IReadOnlyList<TelemetryReadingInput> Readings);

/// <summary>
/// One raw distance sample from a device. <see cref="BootId"/> and <see cref="SequenceNumber"/> together
/// form the idempotency key that lets the server dedupe retried uploads across device reboots.
/// </summary>
public sealed record TelemetryReadingInput(
    Guid BootId,
    long SequenceNumber,
    DateTimeOffset? ObservedAtUtc,
    long UptimeMilliseconds,
    int RawDistanceMm,
    int Quality,
    int SampleCount,
    int WifiRssiDbm,
    IReadOnlyList<string>? ErrorFlags = null);

/// <summary>A single field-level validation failure, optionally scoped to one reading within a batch.</summary>
public sealed record TelemetryValidationError(
    int? ReadingIndex,
    string Field,
    string Code,
    string Message);

/// <summary>Result of validating a telemetry batch before it is persisted.</summary>
public sealed record TelemetryBatchValidationResult(
    IReadOnlyList<TelemetryValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}