using WaterFlex.SaltMonitor.Domain.Monitoring;
using WaterFlex.SaltMonitor.Ingestion;

namespace WaterFlex.SaltMonitor.Operations;

public enum FleetSort
{
    Attention,
    LastReported,
    FillAscending,
    FillDescending,
    Customer
}

public sealed record FleetFilter(
    string? Search = null,
    DeviceReportingStatus? ReportingStatus = null,
    bool? BelowThreshold = null,
    string? LifecycleStatus = null,
    string? FirmwareVersion = null,
    string? DealerExternalId = null);

public sealed record FleetDealerOption(
    string ExternalId,
    string DisplayName);

public sealed record FleetQuery(
    FleetFilter Filter,
    FleetSort Sort = FleetSort.Attention,
    int Page = 1,
    int PageSize = 50);

public sealed record FleetSummary(
    DateTimeOffset GeneratedAtUtc,
    int TotalProvisioned,
    int Active,
    int BelowThreshold,
    int Reporting,
    int Stale,
    int Offline,
    int NeverReported);

public sealed record FleetDeviceListItem(
    Guid DeviceId,
    Guid InstallationId,
    string SerialNumber,
    string Model,
    string LifecycleStatus,
    string? DealerExternalId,
    string DealerName,
    string CustomerDisplayName,
    string? AccountNumber,
    string LocationDisplayName,
    string? AddressSummary,
    string TankLabel,
    int? CapacityPounds,
    double? FillPercent,
    bool IsBelowThreshold,
    DeviceReportingStatus ReportingStatus,
    DateTimeOffset? LastReportedAtUtc,
    int? RawDistanceMm,
    int? Quality,
    int? WifiRssiDbm,
    string? FirmwareVersion,
    IReadOnlyList<string> ErrorFlags,
    SensorHealthStatus SensorStatus,
    SensorFaultCode? SensorFault,
    DateTimeOffset? LastHealthReportedAtUtc,
    bool ClockSynchronized,
    int QueuedReadingCount,
    int DroppedReadingCount);

public sealed record FleetPage(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<FleetDeviceListItem> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record FleetDeviceDetail(
    FleetDeviceListItem Device,
    DateTimeOffset RegisteredAtUtc,
    DateTimeOffset? CommissionedAtUtc,
    DateTimeOffset InstalledAtUtc,
    string? InstalledBy,
    string? WaterFlexWorkOrderId,
    int? CalibrationVersion,
    int? TankDepthMm,
    int? CommissioningDistanceMm,
    DateTimeOffset? CalibrationEffectiveFromUtc,
    bool HasActiveCredential,
    DateTimeOffset? CredentialLastUsedAtUtc,
    string RowVersion);

public sealed record FleetReadingPoint(
    long ReadingId,
    DateTimeOffset TimestampUtc,
    bool UsesObservedTimestamp,
    DateTimeOffset ReceivedAtUtc,
    double FillPercent,
    int RawDistanceMm,
    int Quality,
    int WifiRssiDbm,
    string FirmwareVersion,
    IReadOnlyList<string> ErrorFlags);

public enum TelemetryHistoryResolution
{
    Hour,
    Day
}

public sealed record FleetHistoryPoint(
    DateTimeOffset BucketStartUtc,
    DateTimeOffset BucketEndUtc,
    DateTimeOffset LastReadingAtUtc,
    long ReadingCount,
    double FillPercentMin,
    double FillPercentMax,
    double FillPercentAverage,
    double FillPercentLatest,
    int RawDistanceMmMin,
    int RawDistanceMmMax,
    double RawDistanceMmAverage,
    int WifiRssiDbmMin,
    int WifiRssiDbmMax,
    double WifiRssiDbmAverage,
    int WorstQuality,
    long ErrorCount,
    string LatestFirmwareVersion);

public sealed record FleetHistory(
    TelemetryHistoryResolution Resolution,
    DateTimeOffset FromUtc,
    DateTimeOffset ThroughUtc,
    IReadOnlyList<FleetHistoryPoint> Points);

public interface IFleetQueryService
{
    Task<IReadOnlyList<FleetDealerOption>> GetDealersAsync(
        CancellationToken cancellationToken = default,
        string? scopeDealerExternalId = null);

    Task<FleetSummary> GetSummaryAsync(
        FleetFilter filter,
        CancellationToken cancellationToken = default,
        string? scopeDealerExternalId = null);

    Task<FleetPage> SearchAsync(
        FleetQuery query,
        CancellationToken cancellationToken = default,
        string? scopeDealerExternalId = null);

    Task<FleetDeviceDetail?> GetDeviceAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default,
        string? scopeDealerExternalId = null);

    Task<IReadOnlyList<FleetReadingPoint>?> GetReadingsAsync(
        Guid deviceId,
        TimeSpan range,
        int limit,
        CancellationToken cancellationToken = default,
        string? scopeDealerExternalId = null);

    Task<FleetHistory?> GetHistoryAsync(
        Guid deviceId,
        DateTimeOffset fromUtc,
        TelemetryHistoryResolution resolution,
        CancellationToken cancellationToken = default,
        string? scopeDealerExternalId = null);
}
