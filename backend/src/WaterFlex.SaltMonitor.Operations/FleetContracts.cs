using WaterFlex.SaltMonitor.Domain.Monitoring;
using WaterFlex.SaltMonitor.Ingestion;

namespace WaterFlex.SaltMonitor.Operations;

/// <summary>
/// Ordering for the fleet device list. <see cref="Attention"/> surfaces devices most likely to need
/// staff action (below threshold, stale, or offline) ahead of healthy ones.
/// </summary>
public enum FleetSort
{
    Attention,
    LastReported,
    FillAscending,
    FillDescending,
    Customer
}

/// <summary>Search and filter criteria for browsing the device fleet.</summary>
public sealed record FleetFilter(
    string? Search = null,
    DeviceReportingStatus? ReportingStatus = null,
    bool? BelowThreshold = null,
    string? LifecycleStatus = null,
    string? FirmwareVersion = null,
    string? DealerExternalId = null);

/// <summary>A dealer available as a fleet filter option, scoped to dealers the current staff actor can see.</summary>
public sealed record FleetDealerOption(
    string ExternalId,
    string DisplayName);

/// <summary>A paged, sorted fleet listing request.</summary>
public sealed record FleetQuery(
    FleetFilter Filter,
    FleetSort Sort = FleetSort.Attention,
    int Page = 1,
    int PageSize = 50);

/// <summary>Aggregate counts across the fleet, used for the operations dashboard's headline tiles.</summary>
public sealed record FleetSummary(
    DateTimeOffset GeneratedAtUtc,
    int TotalProvisioned,
    int Active,
    int BelowThreshold,
    int Reporting,
    int Stale,
    int Offline,
    int NeverReported);

/// <summary>A single row in the fleet device list, flattening device, installation, and latest-reading state for display.</summary>
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

/// <summary>One page of the fleet device list.</summary>
public sealed record FleetPage(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<FleetDeviceListItem> Items,
    int TotalCount,
    int Page,
    int PageSize);

/// <summary>
/// Full detail for a single device, including commissioning and calibration history not needed in
/// the list view. <see cref="RowVersion"/> is the optimistic-concurrency token clients must echo
/// back on updates.
/// </summary>
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

/// <summary>
/// A single raw telemetry reading returned for a device's recent-readings view.
/// <see cref="UsesObservedTimestamp"/> indicates whether <see cref="TimestampUtc"/> came from the
/// device's own clock or was derived from server receipt time because the device hadn't synced yet.
/// </summary>
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

/// <summary>Bucketing granularity for aggregated telemetry history charts.</summary>
public enum TelemetryHistoryResolution
{
    Hour,
    Day
}

/// <summary>Aggregated telemetry stats for a single time bucket (hour or day) in a device's history chart.</summary>
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

/// <summary>A bucketed telemetry history series for a device over a time range.</summary>
public sealed record FleetHistory(
    TelemetryHistoryResolution Resolution,
    DateTimeOffset FromUtc,
    DateTimeOffset ThroughUtc,
    IReadOnlyList<FleetHistoryPoint> Points);

/// <summary>
/// Read side of the operations fleet view. Every method accepts an optional
/// <c>scopeDealerExternalId</c> so dealer-scoped staff only ever see their own devices, while
/// unscoped (internal) staff see the whole fleet.
/// </summary>
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
