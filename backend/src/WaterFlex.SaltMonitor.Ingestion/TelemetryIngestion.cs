namespace WaterFlex.SaltMonitor.Ingestion;

/// <summary>Whether a reading was newly persisted or recognized as a retry of one already stored.</summary>
public enum TelemetryReadingStatus
{
    Accepted,
    Duplicate
}

/// <summary>Per-reading acknowledgement echoing the device's own identifiers so it can confirm which samples landed.</summary>
public sealed record TelemetryReadingAcknowledgement(
    Guid BootId,
    long SequenceNumber,
    long ReadingId,
    TelemetryReadingStatus Status,
    double FillPercent,
    DateTimeOffset ReceivedAtUtc);

/// <summary>Server response to an ingested telemetry batch, acknowledging each reading and setting the device's next upload cadence.</summary>
public sealed record TelemetryBatchAcknowledgement(
    DateTimeOffset ServerTimeUtc,
    int NextReportIntervalSeconds,
    IReadOnlyList<TelemetryReadingAcknowledgement> Readings);

/// <summary>Reasons an entire telemetry batch can be rejected before any reading is persisted.</summary>
public enum TelemetryIngestionFailure
{
    None,
    InvalidPayload,
    DeviceUnavailable,
    DeviceNotCommissioned,
    CalibrationUnavailable
}

/// <summary>Outcome of ingesting a telemetry batch for a device.</summary>
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

/// <summary>Applies calibration to raw device readings and persists them as fill-percent telemetry.</summary>
public interface ITelemetryIngestionService
{
    /// <summary>Validates, calibrates, and stores the given batch for a commissioned device.</summary>
    Task<TelemetryIngestionResult> IngestAsync(
        Guid deviceId,
        TelemetryBatch batch,
        CancellationToken cancellationToken = default);
}

/// <summary>Reasons a device's bearer token can fail authentication.</summary>
public enum DeviceTokenFailure
{
    None,
    Invalid,
    Expired,
    Revoked,
    DeviceUnavailable
}

/// <summary>Outcome of validating a device's bearer token.</summary>
public sealed record DeviceTokenValidationResult(
    Guid? DeviceId,
    Guid? CredentialRecordId,
    DeviceTokenFailure Failure)
{
    public bool IsValid => DeviceId.HasValue && Failure == DeviceTokenFailure.None;

    public static DeviceTokenValidationResult Valid(Guid deviceId, Guid credentialRecordId) =>
        new(deviceId, credentialRecordId, DeviceTokenFailure.None);

    public static DeviceTokenValidationResult Failed(DeviceTokenFailure failure) =>
        new(null, null, failure);
}

/// <summary>Authenticates the long-lived bearer tokens issued to devices during commissioning.</summary>
public interface IDeviceTokenValidator
{
    /// <summary>Resolves the given token to a device identity, or reports why it is not currently usable.</summary>
    Task<DeviceTokenValidationResult> ValidateAsync(
        string token,
        CancellationToken cancellationToken = default);
}

/// <summary>Tracks device credential usage (e.g. last-seen time) so stale or unused credentials can be identified.</summary>
public interface IDeviceCredentialUsageRecorder
{
    /// <summary>Records that the given credential was just used to authenticate a request.</summary>
    Task RecordAsync(Guid credentialRecordId, CancellationToken cancellationToken = default);
}
