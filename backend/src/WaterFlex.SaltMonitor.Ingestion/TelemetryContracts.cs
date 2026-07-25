namespace WaterFlex.SaltMonitor.Ingestion;

public sealed record TelemetryBatch(
    int SchemaVersion,
    string FirmwareVersion,
    IReadOnlyList<TelemetryReadingInput> Readings);

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

public sealed record TelemetryValidationError(
    int? ReadingIndex,
    string Field,
    string Code,
    string Message);

public sealed record TelemetryBatchValidationResult(
    IReadOnlyList<TelemetryValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}