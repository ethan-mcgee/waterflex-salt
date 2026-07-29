using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WaterFlex.SaltMonitor.Domain.Level;
using WaterFlex.SaltMonitor.Domain.Model;
using WaterFlex.SaltMonitor.Domain.Monitoring;
using WaterFlex.SaltMonitor.Ingestion;

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence;

public sealed class EfTelemetryIngestionService(
    SaltMonitorDbContext dbContext,
    TelemetryBatchValidator validator,
    TimeProvider timeProvider,
    MonitoringSchedule monitoringSchedule) : ITelemetryIngestionService
{
    public async Task<TelemetryIngestionResult> IngestAsync(
        Guid deviceId,
        TelemetryBatch batch,
        CancellationToken cancellationToken = default)
    {
        var validation = validator.Validate(batch);
        if (!validation.IsValid)
        {
            return TelemetryIngestionResult.Failed(
                TelemetryIngestionFailure.InvalidPayload,
                validation.Errors);
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(() =>
            IngestValidatedAsync(deviceId, batch, retryOnConflict: true, cancellationToken));
    }

    private async Task<TelemetryIngestionResult> IngestValidatedAsync(
        Guid deviceId,
        TelemetryBatch batch,
        bool retryOnConflict,
        CancellationToken cancellationToken)
    {
        var serverTime = timeProvider.GetUtcNow();
        var retry = false;

        await using (var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken))
        {
            try
            {
                var result = await IngestWithinTransactionAsync(
                    deviceId,
                    batch,
                    serverTime,
                    cancellationToken);

                if (!result.IsSuccess)
                {
                    return result;
                }

                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch (DbUpdateException exception)
                when (retryOnConflict && IsUniqueConstraintViolation(exception))
            {
                await transaction.RollbackAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
                retry = true;
            }
        }

        if (retry)
        {
            return await IngestValidatedAsync(
                deviceId,
                batch,
                retryOnConflict: false,
                cancellationToken);
        }

        throw new InvalidOperationException("Telemetry ingestion ended without a result.");
    }

    private async Task<TelemetryIngestionResult> IngestWithinTransactionAsync(
        Guid deviceId,
        TelemetryBatch batch,
        DateTimeOffset serverTime,
        CancellationToken cancellationToken)
    {

        var deviceStatus = await dbContext.Devices
            .Where(device => device.Id == deviceId)
            .Select(device => (DeviceLifecycleStatus?)device.Status)
            .SingleOrDefaultAsync(cancellationToken);

        if (deviceStatus is not DeviceLifecycleStatus.Active)
        {
            return TelemetryIngestionResult.Failed(TelemetryIngestionFailure.DeviceUnavailable);
        }

        var installation = await dbContext.DeviceInstallations
            .SingleOrDefaultAsync(
                candidate => candidate.DeviceId == deviceId && candidate.RemovedAtUtc == null,
                cancellationToken);

        if (installation is null)
        {
            return TelemetryIngestionResult.Failed(TelemetryIngestionFailure.DeviceNotCommissioned);
        }

        var calibrationRecord = await dbContext.TankCalibrations
            .SingleOrDefaultAsync(
                calibration => calibration.DeviceInstallationId == installation.Id
                    && calibration.EffectiveToUtc == null,
                cancellationToken);

        if (calibrationRecord is null)
        {
            return TelemetryIngestionResult.Failed(TelemetryIngestionFailure.CalibrationUnavailable);
        }

        var bootIds = batch.Readings.Select(reading => reading.BootId).Distinct().ToArray();
        var sequenceNumbers = batch.Readings.Select(reading => reading.SequenceNumber).Distinct().ToArray();
        var existingReadings = await dbContext.TelemetryReadings
            .Where(reading => reading.DeviceId == deviceId
                && bootIds.Contains(reading.BootId)
                && sequenceNumbers.Contains(reading.SequenceNumber))
            .ToDictionaryAsync(
                reading => (reading.BootId, reading.SequenceNumber),
                cancellationToken);

        var calibration = new TankCalibration(calibrationRecord.TankDepthMm);
        var acknowledgements = new TelemetryReadingAcknowledgement?[batch.Readings.Count];
        var pending = new List<(int Index, TelemetryReadingRecord Reading)>();

        for (var index = 0; index < batch.Readings.Count; index++)
        {
            var input = batch.Readings[index];
            if (existingReadings.TryGetValue((input.BootId, input.SequenceNumber), out var existing))
            {
                acknowledgements[index] = new(
                    input.BootId,
                    input.SequenceNumber,
                    existing.Id,
                    TelemetryReadingStatus.Duplicate,
                    existing.FillPercent,
                    existing.ReceivedAtUtc);
                continue;
            }

            var reading = new TelemetryReadingRecord
            {
                DeviceId = deviceId,
                DeviceInstallationId = installation.Id,
                TankCalibrationRecordId = calibrationRecord.Id,
                BootId = input.BootId,
                SequenceNumber = input.SequenceNumber,
                ObservedAtUtc = input.ObservedAtUtc,
                ReceivedAtUtc = serverTime,
                UptimeMilliseconds = input.UptimeMilliseconds,
                RawDistanceMm = input.RawDistanceMm,
                FillPercent = FillCalculator.CalculateFillPercent(input.RawDistanceMm, calibration),
                Quality = input.Quality,
                SampleCount = input.SampleCount,
                WifiRssiDbm = input.WifiRssiDbm,
                FirmwareVersion = batch.FirmwareVersion,
                ErrorFlagsJson = JsonSerializer.Serialize(input.ErrorFlags ?? [])
            };

            dbContext.TelemetryReadings.Add(reading);
            pending.Add((index, reading));
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var (index, reading) in pending)
        {
            acknowledgements[index] = new(
                reading.BootId,
                reading.SequenceNumber,
                reading.Id,
                TelemetryReadingStatus.Accepted,
                reading.FillPercent,
                reading.ReceivedAtUtc);
        }

        return TelemetryIngestionResult.Success(new(
            serverTime,
            monitoringSchedule.ReportIntervalSeconds,
            acknowledgements.Select(acknowledgement => acknowledgement!).ToArray()));
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: "23505" };
}