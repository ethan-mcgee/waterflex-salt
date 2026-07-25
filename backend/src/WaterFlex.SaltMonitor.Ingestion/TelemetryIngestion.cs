namespace WaterFlex.SaltMonitor.Ingestion;

public enum TelemetryReadingStatus
{
    Accepted,
    Duplicate
}

public sealed record TelemetryReadingAcknowledgement(
    Guid BootId,
    long SequenceNumber,
    long ReadingId,
    TelemetryReadingStatus Status,
    double FillPercent,
    DateTimeOffset ReceivedAtUtc);

public sealed record TelemetryBatchAcknowledgement(
    DateTimeOffset ServerTimeUtc,
    int NextReportIntervalSeconds,
    IReadOnlyList<TelemetryReadingAcknowledgement> Readings);

public enum TelemetryIngestionFailure
{
    None,
    InvalidPayload,
    DeviceUnavailable,
    DeviceNotCommissioned,
    CalibrationUnavailable
}

public sealed record TelemetryIngestionResult(
    TelemetryBatchAcknowledgement? Acknowledgement,
    TelemetryIngestionFailure Failure,
    IReadOnlyList<TelemetryValidationError> ValidationErrors)
{
    public bool IsSuccess => Failure == TelemetryIngestionFailure.None;

    public static TelemetryIngestionResult Success(TelemetryBatchAcknowledgement acknowledgement) =>
        new(acknowledgement, TelemetryIngestionFailure.None, []);

    public static TelemetryIngestionResult Failed(
        TelemetryIngestionFailure failure,
        IReadOnlyList<TelemetryValidationError>? validationErrors = null) =>
        new(null, failure, validationErrors ?? []);
}

public interface ITelemetryIngestionService
{
    Task<TelemetryIngestionResult> IngestAsync(
        Guid deviceId,
        TelemetryBatch batch,
        CancellationToken cancellationToken = default);
}

public enum DeviceTokenFailure
{
    None,
    Invalid,
    Expired,
    Revoked,
    DeviceUnavailable
}

public sealed record DeviceTokenValidationResult(
    Guid? DeviceId,
    DeviceTokenFailure Failure)
{
    public bool IsValid => DeviceId.HasValue && Failure == DeviceTokenFailure.None;

    public static DeviceTokenValidationResult Valid(Guid deviceId) =>
        new(deviceId, DeviceTokenFailure.None);

    public static DeviceTokenValidationResult Failed(DeviceTokenFailure failure) =>
        new(null, failure);
}

public interface IDeviceTokenValidator
{
    Task<DeviceTokenValidationResult> ValidateAsync(
        string token,
        CancellationToken cancellationToken = default);
}