using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence;

public sealed class TelemetryHistoryOptions
{
    public const string SectionName = "TelemetryHistory";
    public int RawRetentionDays { get; set; } = 30;
    public int HourlyRetentionMonths { get; set; } = 13;
    public int DailyRetentionYears { get; set; } = 3;
    public int DeleteBatchSize { get; set; } = 10_000;
    public int MaintenanceIntervalMinutes { get; set; } = 15;
}

public sealed record TelemetryMaintenanceResult(
    int HourlyBucketsWritten,
    int DailyBucketsWritten,
    int RawReadingsDeleted,
    int HourlyBucketsDeleted,
    int DailyBucketsDeleted,
    DateTimeOffset? OldestRawReadingUtc,
    DateTimeOffset? OldestHourlyBucketUtc,
    DateTimeOffset? OldestDailyBucketUtc,
    bool SkippedBecauseAlreadyRunning);

public interface ITelemetryHistoryMaintenanceService
{
    Task<TelemetryMaintenanceResult> RunAsync(CancellationToken cancellationToken = default);
}

public sealed class TelemetryHistoryMaintenanceService(
    SaltMonitorDbContext dbContext,
    TimeProvider timeProvider,
    IOptions<TelemetryHistoryOptions> options,
    ILogger<TelemetryHistoryMaintenanceService> logger) : ITelemetryHistoryMaintenanceService
{
    private const string BackfillStateName = "telemetry-history-backfill-v1";
    private const long AdvisoryLockId = 1_465_011_442;
    private readonly TelemetryHistoryOptions historyOptions = options.Value;

    public async Task<TelemetryMaintenanceResult> RunAsync(CancellationToken cancellationToken = default)
    {
        ValidateOptions();
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        if (!await TrySetAdvisoryLockAsync(true, cancellationToken))
        {
            return new(0, 0, 0, 0, 0, null, null, null, true);
        }

        try
        {
            var now = timeProvider.GetUtcNow();
            var completedHour = TruncateHour(now);
            var completedDay = TruncateDay(now);
            var backfillComplete = await dbContext.TelemetryMaintenanceStates
                .AsNoTracking()
                .AnyAsync(state => state.Name == BackfillStateName, cancellationToken);
            var hourlyFromUtc = backfillComplete ? completedHour.AddHours(-2) : DateTimeOffset.UnixEpoch;
            var dailyFromUtc = backfillComplete ? completedDay.AddDays(-2) : DateTimeOffset.UnixEpoch;

            var hourlyWritten = await RollUpHourlyAsync(hourlyFromUtc, completedHour, now, cancellationToken);
            var dailyWritten = await RollUpDailyAsync(dailyFromUtc, completedDay, now, cancellationToken);

            if (!backfillComplete)
            {
                dbContext.TelemetryMaintenanceStates.Add(new()
                {
                    Name = BackfillStateName,
                    CompletedAtUtc = now
                });
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            var rawDeleted = await DeleteRawReadingsAsync(now.AddDays(-historyOptions.RawRetentionDays), cancellationToken);
            var hourlyDeleted = await DeleteSummariesAsync(
                "TelemetryHourlySummaries",
                now.AddMonths(-historyOptions.HourlyRetentionMonths),
                cancellationToken);
            var dailyDeleted = await DeleteSummariesAsync(
                "TelemetryDailySummaries",
                now.AddYears(-historyOptions.DailyRetentionYears),
                cancellationToken);

            var oldestRaw = await dbContext.TelemetryReadings.MinAsync(
                reading => (DateTimeOffset?)reading.ReceivedAtUtc,
                cancellationToken);
            var oldestHourly = await dbContext.TelemetryHourlySummaries.MinAsync(
                summary => (DateTimeOffset?)summary.BucketStartUtc,
                cancellationToken);
            var oldestDaily = await dbContext.TelemetryDailySummaries.MinAsync(
                summary => (DateTimeOffset?)summary.BucketStartUtc,
                cancellationToken);

            logger.LogInformation(
                "Telemetry history maintenance completed: hourly={HourlyWritten}, daily={DailyWritten}, rawDeleted={RawDeleted}, hourlyDeleted={HourlyDeleted}, dailyDeleted={DailyDeleted}, oldestRaw={OldestRaw}, oldestHourly={OldestHourly}, oldestDaily={OldestDaily}",
                hourlyWritten,
                dailyWritten,
                rawDeleted,
                hourlyDeleted,
                dailyDeleted,
                oldestRaw,
                oldestHourly,
                oldestDaily);

            return new(
                hourlyWritten,
                dailyWritten,
                rawDeleted,
                hourlyDeleted,
                dailyDeleted,
                oldestRaw,
                oldestHourly,
                oldestDaily,
                false);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Telemetry history maintenance failed");
            throw;
        }
        finally
        {
            await TrySetAdvisoryLockAsync(false, CancellationToken.None);
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private Task<int> RollUpHourlyAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset throughUtc,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO "TelemetryHourlySummaries" (
                "DeviceId", "BucketStartUtc", "LastReadingAtUtc", "ReadingCount",
                "FillPercentMin", "FillPercentMax", "FillPercentAverage", "FillPercentLatest",
                "RawDistanceMmMin", "RawDistanceMmMax", "RawDistanceMmAverage",
                "WifiRssiDbmMin", "WifiRssiDbmMax", "WifiRssiDbmAverage",
                "WorstQuality", "ErrorCount", "LatestFirmwareVersion", "UpdatedAtUtc")
            SELECT
                "DeviceId",
                date_trunc('hour', "ReceivedAtUtc" AT TIME ZONE 'UTC') AT TIME ZONE 'UTC',
                max("ReceivedAtUtc"),
                count(*),
                min("FillPercent"), max("FillPercent"), avg("FillPercent"),
                (array_agg("FillPercent" ORDER BY "ReceivedAtUtc" DESC, "Id" DESC))[1],
                min("RawDistanceMm"), max("RawDistanceMm"), avg("RawDistanceMm"),
                min("WifiRssiDbm"), max("WifiRssiDbm"), avg("WifiRssiDbm"),
                min("Quality"),
                count(*) FILTER (WHERE "ErrorFlagsJson" <> '[]'),
                (array_agg("FirmwareVersion" ORDER BY "ReceivedAtUtc" DESC, "Id" DESC))[1],
                {{updatedAtUtc}}
            FROM "TelemetryReadings"
            WHERE "ReceivedAtUtc" >= {{fromUtc}} AND "ReceivedAtUtc" < {{throughUtc}}
            GROUP BY "DeviceId", date_trunc('hour', "ReceivedAtUtc" AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'
            ON CONFLICT ("DeviceId", "BucketStartUtc") DO UPDATE SET
                "LastReadingAtUtc" = EXCLUDED."LastReadingAtUtc",
                "ReadingCount" = EXCLUDED."ReadingCount",
                "FillPercentMin" = EXCLUDED."FillPercentMin",
                "FillPercentMax" = EXCLUDED."FillPercentMax",
                "FillPercentAverage" = EXCLUDED."FillPercentAverage",
                "FillPercentLatest" = EXCLUDED."FillPercentLatest",
                "RawDistanceMmMin" = EXCLUDED."RawDistanceMmMin",
                "RawDistanceMmMax" = EXCLUDED."RawDistanceMmMax",
                "RawDistanceMmAverage" = EXCLUDED."RawDistanceMmAverage",
                "WifiRssiDbmMin" = EXCLUDED."WifiRssiDbmMin",
                "WifiRssiDbmMax" = EXCLUDED."WifiRssiDbmMax",
                "WifiRssiDbmAverage" = EXCLUDED."WifiRssiDbmAverage",
                "WorstQuality" = EXCLUDED."WorstQuality",
                "ErrorCount" = EXCLUDED."ErrorCount",
                "LatestFirmwareVersion" = EXCLUDED."LatestFirmwareVersion",
                "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc";
            """, cancellationToken);

    private Task<int> RollUpDailyAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset throughUtc,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO "TelemetryDailySummaries" (
                "DeviceId", "BucketStartUtc", "LastReadingAtUtc", "ReadingCount",
                "FillPercentMin", "FillPercentMax", "FillPercentAverage", "FillPercentLatest",
                "RawDistanceMmMin", "RawDistanceMmMax", "RawDistanceMmAverage",
                "WifiRssiDbmMin", "WifiRssiDbmMax", "WifiRssiDbmAverage",
                "WorstQuality", "ErrorCount", "LatestFirmwareVersion", "UpdatedAtUtc")
            SELECT
                "DeviceId",
                date_trunc('day', "BucketStartUtc" AT TIME ZONE 'UTC') AT TIME ZONE 'UTC',
                max("LastReadingAtUtc"),
                sum("ReadingCount"),
                min("FillPercentMin"), max("FillPercentMax"),
                sum("FillPercentAverage" * "ReadingCount") / sum("ReadingCount"),
                (array_agg("FillPercentLatest" ORDER BY "LastReadingAtUtc" DESC))[1],
                min("RawDistanceMmMin"), max("RawDistanceMmMax"),
                sum("RawDistanceMmAverage" * "ReadingCount") / sum("ReadingCount"),
                min("WifiRssiDbmMin"), max("WifiRssiDbmMax"),
                sum("WifiRssiDbmAverage" * "ReadingCount") / sum("ReadingCount"),
                min("WorstQuality"), sum("ErrorCount"),
                (array_agg("LatestFirmwareVersion" ORDER BY "LastReadingAtUtc" DESC))[1],
                {{updatedAtUtc}}
            FROM "TelemetryHourlySummaries"
            WHERE "BucketStartUtc" >= {{fromUtc}} AND "BucketStartUtc" < {{throughUtc}}
            GROUP BY "DeviceId", date_trunc('day', "BucketStartUtc" AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'
            ON CONFLICT ("DeviceId", "BucketStartUtc") DO UPDATE SET
                "LastReadingAtUtc" = EXCLUDED."LastReadingAtUtc",
                "ReadingCount" = EXCLUDED."ReadingCount",
                "FillPercentMin" = EXCLUDED."FillPercentMin",
                "FillPercentMax" = EXCLUDED."FillPercentMax",
                "FillPercentAverage" = EXCLUDED."FillPercentAverage",
                "FillPercentLatest" = EXCLUDED."FillPercentLatest",
                "RawDistanceMmMin" = EXCLUDED."RawDistanceMmMin",
                "RawDistanceMmMax" = EXCLUDED."RawDistanceMmMax",
                "RawDistanceMmAverage" = EXCLUDED."RawDistanceMmAverage",
                "WifiRssiDbmMin" = EXCLUDED."WifiRssiDbmMin",
                "WifiRssiDbmMax" = EXCLUDED."WifiRssiDbmMax",
                "WifiRssiDbmAverage" = EXCLUDED."WifiRssiDbmAverage",
                "WorstQuality" = EXCLUDED."WorstQuality",
                "ErrorCount" = EXCLUDED."ErrorCount",
                "LatestFirmwareVersion" = EXCLUDED."LatestFirmwareVersion",
                "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc";
            """, cancellationToken);

    private async Task<int> DeleteRawReadingsAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken)
    {
        var totalDeleted = 0;
        while (true)
        {
            var deleted = await dbContext.Database.ExecuteSqlInterpolatedAsync($$"""
                WITH doomed AS (
                    SELECT raw."Id"
                    FROM "TelemetryReadings" raw
                    WHERE raw."ReceivedAtUtc" < {{cutoffUtc}}
                      AND EXISTS (
                          SELECT 1 FROM "TelemetryHourlySummaries" hourly
                          WHERE hourly."DeviceId" = raw."DeviceId"
                            AND hourly."BucketStartUtc" = date_trunc('hour', raw."ReceivedAtUtc" AT TIME ZONE 'UTC') AT TIME ZONE 'UTC')
                      AND EXISTS (
                          SELECT 1 FROM "TelemetryDailySummaries" daily
                          WHERE daily."DeviceId" = raw."DeviceId"
                            AND daily."BucketStartUtc" = date_trunc('day', raw."ReceivedAtUtc" AT TIME ZONE 'UTC') AT TIME ZONE 'UTC')
                    ORDER BY raw."ReceivedAtUtc", raw."Id"
                    LIMIT {{historyOptions.DeleteBatchSize}}
                    FOR UPDATE SKIP LOCKED)
                DELETE FROM "TelemetryReadings" raw
                USING doomed
                WHERE raw."Id" = doomed."Id";
                """, cancellationToken);
            totalDeleted += deleted;
            if (deleted < historyOptions.DeleteBatchSize)
            {
                return totalDeleted;
            }
        }
    }

    private async Task<int> DeleteSummariesAsync(
        string tableName,
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken)
    {
        if (tableName is not ("TelemetryHourlySummaries" or "TelemetryDailySummaries"))
        {
            throw new ArgumentOutOfRangeException(nameof(tableName));
        }

        var totalDeleted = 0;
        while (true)
        {
#pragma warning disable EF1002 // tableName is restricted to the two literals above; SQL parameters cannot represent identifiers.
            var deleted = await dbContext.Database.ExecuteSqlRawAsync($$"""
                WITH doomed AS (
                    SELECT "DeviceId", "BucketStartUtc"
                    FROM "{{tableName}}"
                    WHERE "BucketStartUtc" < {0}
                    ORDER BY "BucketStartUtc"
                    LIMIT {1}
                    FOR UPDATE SKIP LOCKED)
                DELETE FROM "{{tableName}}" summary
                USING doomed
                WHERE summary."DeviceId" = doomed."DeviceId"
                  AND summary."BucketStartUtc" = doomed."BucketStartUtc";
                """, [cutoffUtc, historyOptions.DeleteBatchSize], cancellationToken);
#pragma warning restore EF1002
            totalDeleted += deleted;
            if (deleted < historyOptions.DeleteBatchSize)
            {
                return totalDeleted;
            }
        }
    }

    private async Task<bool> TrySetAdvisoryLockAsync(bool acquire, CancellationToken cancellationToken)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = acquire
            ? "SELECT pg_try_advisory_lock(@lock_id);"
            : "SELECT pg_advisory_unlock(@lock_id);";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "lock_id";
        parameter.DbType = DbType.Int64;
        parameter.Value = AdvisoryLockId;
        command.Parameters.Add(parameter);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private void ValidateOptions()
    {
        if (historyOptions.RawRetentionDays < 1
            || historyOptions.HourlyRetentionMonths < 1
            || historyOptions.DailyRetentionYears < 1
            || historyOptions.DeleteBatchSize is < 1 or > 100_000
            || historyOptions.MaintenanceIntervalMinutes < 1)
        {
            throw new InvalidOperationException("TelemetryHistory retention and maintenance settings are invalid.");
        }
    }

    private static DateTimeOffset TruncateHour(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;
        return new(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
    }

    private static DateTimeOffset TruncateDay(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;
        return new(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);
    }
}
