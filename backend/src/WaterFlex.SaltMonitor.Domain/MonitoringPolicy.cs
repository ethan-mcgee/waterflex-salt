namespace WaterFlex.SaltMonitor.Domain.Monitoring;

public enum DeviceReportingStatus
{
    Reporting,
    Stale,
    Offline,
    NeverReported
}

public static class MonitoringPolicy
{
    public const double LowFillThresholdPercent = 35.0;

    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(2);
    public static readonly TimeSpan OfflineAfter = TimeSpan.FromHours(6);

    public static bool IsBelowFillThreshold(double fillPercent) =>
        fillPercent < LowFillThresholdPercent;

    public static DeviceReportingStatus GetReportingStatus(
        DateTimeOffset? lastReportedAtUtc,
        DateTimeOffset nowUtc)
    {
        if (lastReportedAtUtc is null)
        {
            return DeviceReportingStatus.NeverReported;
        }

        var age = nowUtc - lastReportedAtUtc.Value;
        if (age <= StaleAfter)
        {
            return DeviceReportingStatus.Reporting;
        }

        return age <= OfflineAfter
            ? DeviceReportingStatus.Stale
            : DeviceReportingStatus.Offline;
    }
}