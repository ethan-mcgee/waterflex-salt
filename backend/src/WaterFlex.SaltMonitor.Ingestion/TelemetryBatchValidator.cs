namespace WaterFlex.SaltMonitor.Ingestion;

public sealed class TelemetryBatchValidator(TimeProvider timeProvider)
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumReadingsPerBatch = 50;

    public TelemetryBatchValidationResult Validate(TelemetryBatch? batch)
    {
        var errors = new List<TelemetryValidationError>();

        if (batch is null)
        {
            errors.Add(new(null, "body", "required", "A telemetry batch is required."));
            return new(errors);
        }

        if (batch.SchemaVersion != CurrentSchemaVersion)
        {
            errors.Add(new(
                null,
                nameof(batch.SchemaVersion),
                "unsupported_schema_version",
                $"Schema version {CurrentSchemaVersion} is required."));
        }

        if (string.IsNullOrWhiteSpace(batch.FirmwareVersion) || batch.FirmwareVersion.Length > 64)
        {
            errors.Add(new(
                null,
                nameof(batch.FirmwareVersion),
                "invalid_firmware_version",
                "Firmware version must contain between 1 and 64 characters."));
        }

        if (batch.Readings is null || batch.Readings.Count == 0)
        {
            errors.Add(new(null, nameof(batch.Readings), "required", "At least one reading is required."));
            return new(errors);
        }

        if (batch.Readings.Count > MaximumReadingsPerBatch)
        {
            errors.Add(new(
                null,
                nameof(batch.Readings),
                "batch_too_large",
                $"A batch cannot contain more than {MaximumReadingsPerBatch} readings."));
        }

        var now = timeProvider.GetUtcNow();
        var readingKeys = new HashSet<(Guid BootId, long SequenceNumber)>();
        for (var index = 0; index < batch.Readings.Count; index++)
        {
            var reading = batch.Readings[index];
            ValidateReading(reading, index, now, errors);

            if (reading is not null && !readingKeys.Add((reading.BootId, reading.SequenceNumber)))
            {
                errors.Add(new(
                    index,
                    nameof(reading.SequenceNumber),
                    "duplicate_reading_key",
                    "Boot ID and sequence number must be unique within a batch."));
            }
        }

        return new(errors);
    }

    private static void ValidateReading(
        TelemetryReadingInput? reading,
        int index,
        DateTimeOffset now,
        ICollection<TelemetryValidationError> errors)
    {
        if (reading is null)
        {
            errors.Add(new(index, "reading", "required", "A reading is required."));
            return;
        }

        if (reading.BootId == Guid.Empty)
        {
            errors.Add(new(index, nameof(reading.BootId), "required", "Boot ID cannot be empty."));
        }

        if (reading.SequenceNumber < 0)
        {
            errors.Add(new(index, nameof(reading.SequenceNumber), "out_of_range", "Sequence number cannot be negative."));
        }

        if (reading.ObservedAtUtc is { } observedAt && observedAt > now.AddMinutes(5))
        {
            errors.Add(new(index, nameof(reading.ObservedAtUtc), "future_timestamp", "Observation time cannot be more than five minutes in the future."));
        }

        if (reading.UptimeMilliseconds < 0)
        {
            errors.Add(new(index, nameof(reading.UptimeMilliseconds), "out_of_range", "Uptime cannot be negative."));
        }

        if (reading.RawDistanceMm is < 30 or > 4500)
        {
            errors.Add(new(index, nameof(reading.RawDistanceMm), "out_of_range", "Raw distance must be between 30 and 4500 millimeters."));
        }

        if (reading.Quality is < 0 or > 100)
        {
            errors.Add(new(index, nameof(reading.Quality), "out_of_range", "Quality must be between 0 and 100."));
        }

        if (reading.SampleCount is < 1 or > 1024)
        {
            errors.Add(new(index, nameof(reading.SampleCount), "out_of_range", "Sample count must be between 1 and 1024."));
        }

        if (reading.WifiRssiDbm is < -127 or > 0)
        {
            errors.Add(new(index, nameof(reading.WifiRssiDbm), "out_of_range", "Wi-Fi RSSI must be between -127 and 0 dBm."));
        }

        if (reading.ErrorFlags is not { } errorFlags)
        {
            return;
        }

        if (errorFlags.Count > 16)
        {
            errors.Add(new(index, nameof(reading.ErrorFlags), "too_many_items", "At most 16 error flags are allowed."));
        }

        if (errorFlags.Any(flag => string.IsNullOrWhiteSpace(flag) || flag.Length > 64))
        {
            errors.Add(new(index, nameof(reading.ErrorFlags), "invalid_item", "Error flags must contain between 1 and 64 characters."));
        }
    }
}