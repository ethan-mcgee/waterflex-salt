namespace WaterFlex.SaltMonitor.Domain.Monitoring;

/// <summary>How recently a device has checked in, relative to its expected report interval.</summary>
public enum DeviceReportingStatus
{
    Reporting,
    Stale,
    Offline,
    NeverReported
}

/// <summary>
/// Derives the stale/offline thresholds for a device from its expected report interval, so that a
/// device configured to report more or less frequently is judged against its own cadence rather than
/// a fixed clock.
/// </summary>
public sealed class MonitoringSchedule
{
    public const int DefaultReportIntervalSeconds = 60;
    public const int StaleAfterMissedReports = 3;
    public const int OfflineAfterMissedReports = 5;

    public MonitoringSchedule(TimeSpan reportInterval)
    {
        if (reportInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reportInterval),
                "Report interval must be greater than zero.");
        }

        ReportInterval = reportInterval;
    }

    public TimeSpan ReportInterval { get; }
    public int ReportIntervalSeconds => checked((int)ReportInterval.TotalSeconds);
    public TimeSpan StaleAfter => ReportInterval * StaleAfterMissedReports;
    public TimeSpan OfflineAfter => ReportInterval * OfflineAfterMissedReports;
}

/// <summary>Central home for the tunable thresholds that decide when a tank needs salt and when a device is considered unreachable.</summary>
public static class MonitoringPolicy
{
    public const double LowFillThresholdPercent = 35.0;

    public static bool IsBelowFillThreshold(double fillPercent) =>
        fillPercent < LowFillThresholdPercent;

    /// <summary>Classifies a device's reporting status from how long it has been since its last report, per its own schedule.</summary>
    public static DeviceReportingStatus GetReportingStatus(
        DateTimeOffset? lastReportedAtUtc,
        DateTimeOffset nowUtc,
        MonitoringSchedule schedule)
    {
        if (lastReportedAtUtc is null)
        {
            return DeviceReportingStatus.NeverReported;
        }

        var age = nowUtc - lastReportedAtUtc.Value;
        if (age < schedule.StaleAfter)
        {
            return DeviceReportingStatus.Reporting;
        }

        return age < schedule.OfflineAfter
            ? DeviceReportingStatus.Stale
            : DeviceReportingStatus.Offline;
    }
}

/// <summary>Lifecycle states of a low-salt alert, from first detection through staff disposition. See Plan C.</summary>
public enum LowSaltAlertStatus
{
    Open,
    Acknowledged,
    Approved,
    Dismissed,
    Resolved
}

/// <summary>Lifecycle states of a delivery ticket raised against the external delivery-ticket gateway for an approved alert.</summary>
public enum DeliveryTicketStatus
{
    Pending,
    Created,
    Resolved,
    Failed
}
