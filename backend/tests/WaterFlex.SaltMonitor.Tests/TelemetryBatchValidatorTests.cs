using WaterFlex.SaltMonitor.Ingestion;
using Xunit;

namespace WaterFlex.SaltMonitor.Tests;

public sealed class TelemetryBatchValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);
    private readonly TelemetryBatchValidator _validator = new(new FixedTimeProvider(Now));

    [Fact]
    public void Validate_AcceptsValidBatch()
    {
        var result = _validator.Validate(CreateBatch(CreateReading()));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_ReportsReadingFieldErrors()
    {
        var result = _validator.Validate(CreateBatch(CreateReading() with
        {
            RawDistanceMm = 5000,
            Quality = 101,
            ObservedAtUtc = Now.AddMinutes(6)
        }));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Field == nameof(TelemetryReadingInput.RawDistanceMm));
        Assert.Contains(result.Errors, error => error.Field == nameof(TelemetryReadingInput.Quality));
        Assert.Contains(result.Errors, error => error.Field == nameof(TelemetryReadingInput.ObservedAtUtc));
    }

    [Fact]
    public void Validate_RejectsDuplicateKeysWithinBatch()
    {
        var reading = CreateReading();
        var result = _validator.Validate(CreateBatch(reading, reading));

        Assert.Contains(result.Errors, error => error.Code == "duplicate_reading_key");
    }

    [Fact]
    public void Validate_RejectsFaultedOrLowQualitySamplesAsMeasurements()
    {
        var result = _validator.Validate(CreateBatch(CreateReading() with
        {
            Quality = TelemetryBatchValidator.MinimumOperationalQuality - 1,
            ErrorFlags = ["sensor_timeout"]
        }));

        Assert.Contains(result.Errors, error => error.Code == "quality_too_low");
        Assert.Contains(result.Errors, error => error.Code == "sensor_fault_not_measurement");
    }

    private static TelemetryBatch CreateBatch(params TelemetryReadingInput[] readings) =>
        new(1, "1.0.0", readings);

    private static TelemetryReadingInput CreateReading() =>
        new(Guid.NewGuid(), 1, Now, 1000, 1000, 90, 8, -60);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
