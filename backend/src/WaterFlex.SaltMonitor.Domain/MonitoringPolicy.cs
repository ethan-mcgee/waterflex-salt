namespace WaterFlex.SaltMonitor.Domain.Monitoring;

public enum DeviceReportingStatus
{
    Reporting,
    Stale,
    Offline,
    NeverReported
}

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

public static class MonitoringPolicy
{
    public const double LowFillThresholdPercent = 35.0;

    public static bool IsBelowFillThreshold(double fillPercent) =>
        fillPercent < LowFillThresholdPercent;

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

public enum LowSaltAlertStatus
{
    Open,
    Acknowledged,
    Approved,
    Dismissed,
    Resolved
}
