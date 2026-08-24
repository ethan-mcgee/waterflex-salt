using Microsoft.EntityFrameworkCore;
using WaterFlex.SaltMonitor.Domain.Monitoring;
using WaterFlex.SaltMonitor.Ingestion;

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence;

public sealed class EfDeviceHealthService(
    SaltMonitorDbContext dbContext,
    TimeProvider timeProvider,
    MonitoringSchedule monitoringSchedule) : IDeviceHealthService
{
    public async Task<DeviceHealthResult> ReportAsync(
        Guid deviceId,
        DeviceHealthHeartbeat heartbeat,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var errors = Validate(heartbeat, now);
        if (errors.Count > 0)
        {
            return DeviceHealthResult.Failed(DeviceHealthFailure.InvalidPayload, errors);
        }

        var device = await dbContext.Devices.SingleOrDefaultAsync(
            candidate => candidate.Id == deviceId,
            cancellationToken);
        if (device is null || device.Status != DeviceLifecycleStatus.Active)
        {
            return DeviceHealthResult.Failed(DeviceHealthFailure.DeviceUnavailable);
        }

        device.LastHealthReportedAtUtc = now;
        device.LastDeviceReportedAtUtc = heartbeat.ReportedAtUtc;
        device.LastSensorStatus = heartbeat.SensorStatus;
        device.LastSensorFault = heartbeat.SensorFault;
        device.LastHealthFirmwareVersion = heartbeat.FirmwareVersion.Trim();
        device.LastHealthWifiRssiDbm = heartbeat.WifiRssiDbm;
        device.LastQueuedReadingCount = heartbeat.QueuedReadingCount;
        device.LastDroppedReadingCount = heartbeat.DroppedReadingCount;
        device.LastClockSynchronized = heartbeat.ClockSynchronized;
        await dbContext.SaveChangesAsync(cancellationToken);

        return DeviceHealthResult.Success(new(now, monitoringSchedule.ReportIntervalSeconds));
    }

    private static IReadOnlyList<TelemetryValidationError> Validate(
        DeviceHealthHeartbeat heartbeat,
        DateTimeOffset now)
    {
        var errors = new List<TelemetryValidationError>();
        if (heartbeat.SchemaVersion != 1)
        {
            errors.Add(new(null, nameof(heartbeat.SchemaVersion), "unsupported_schema_version", "Schema version 1 is required."));
        }
        if (string.IsNullOrWhiteSpace(heartbeat.FirmwareVersion) || heartbeat.FirmwareVersion.Length > 64)
        {
            errors.Add(new(null, nameof(heartbeat.FirmwareVersion), "invalid_firmware_version", "Firmware version must contain between 1 and 64 characters."));
        }
        if (heartbeat.ReportedAtUtc is { } reportedAt && reportedAt > now.AddMinutes(5))
        {
            errors.Add(new(null, nameof(heartbeat.ReportedAtUtc), "future_timestamp", "Reported time cannot be more than five minutes in the future."));
        }
        if (heartbeat.UptimeMilliseconds < 0)
        {
            errors.Add(new(null, nameof(heartbeat.UptimeMilliseconds), "out_of_range", "Uptime cannot be negative."));
        }
        if (heartbeat.WifiRssiDbm is < -127 or > 0)
        {
            errors.Add(new(null, nameof(heartbeat.WifiRssiDbm), "out_of_range", "Wi-Fi RSSI must be between -127 and 0 dBm."));
        }
        if (heartbeat.QueuedReadingCount is < 0 or > 10_000)
        {
            errors.Add(new(null, nameof(heartbeat.QueuedReadingCount), "out_of_range", "Queued reading count must be between 0 and 10000."));
        }
        if (heartbeat.DroppedReadingCount is < 0 or > 100_000_000)
        {
            errors.Add(new(null, nameof(heartbeat.DroppedReadingCount), "out_of_range", "Dropped reading count must be between 0 and 100000000."));
        }
        if (!Enum.IsDefined(heartbeat.SensorStatus))
        {
            errors.Add(new(null, nameof(heartbeat.SensorStatus), "invalid_value", "Sensor status is not recognized."));
        }
        if (heartbeat.SensorFault is { } sensorFault && !Enum.IsDefined(sensorFault))
        {
            errors.Add(new(null, nameof(heartbeat.SensorFault), "invalid_value", "Sensor fault is not recognized."));
        }
        if (heartbeat.SensorStatus == SensorHealthStatus.Faulted && heartbeat.SensorFault is null)
        {
            errors.Add(new(null, nameof(heartbeat.SensorFault), "required", "A sensor fault is required when the sensor is faulted."));
        }
        if (heartbeat.SensorStatus != SensorHealthStatus.Faulted && heartbeat.SensorFault is not null)
        {
            errors.Add(new(null, nameof(heartbeat.SensorFault), "not_permitted", "A sensor fault is permitted only when the sensor is faulted."));
        }
        return errors;
    }
}
