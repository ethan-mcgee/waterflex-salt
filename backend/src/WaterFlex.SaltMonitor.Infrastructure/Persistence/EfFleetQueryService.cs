using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WaterFlex.SaltMonitor.Domain.Monitoring;
using WaterFlex.SaltMonitor.Ingestion;
using WaterFlex.SaltMonitor.Operations;

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence;

public sealed class EfFleetQueryService(
    SaltMonitorDbContext dbContext,
    TimeProvider timeProvider,
    MonitoringSchedule monitoringSchedule) : IFleetQueryService
{
    public async Task<IReadOnlyList<FleetDealerOption>> GetDealersAsync(
        CancellationToken cancellationToken = default,
        string? scopeDealerExternalId = null) =>
        await dbContext.Dealers
            .AsNoTracking()
            .Where(dealer => dealer.IsActive)
            .Where(dealer => scopeDealerExternalId == null || dealer.ExternalId == scopeDealerExternalId)
            .OrderBy(dealer => dealer.DisplayName)
            .Select(dealer => new FleetDealerOption(dealer.ExternalId, dealer.DisplayName))
            .ToArrayAsync(cancellationToken);

    public async Task<FleetSummary> GetSummaryAsync(
        FleetFilter filter,
        CancellationToken cancellationToken = default,
        string? scopeDealerExternalId = null)
    {
        var effectiveFilter = scopeDealerExternalId is null
            ? filter
            : filter with { DealerExternalId = scopeDealerExternalId };
        var now = timeProvider.GetUtcNow();
        var items = ApplyFilter(await LoadFleetAsync(now, cancellationToken), effectiveFilter).ToArray();

        return new(
            now,
            items.Length,
            items.Count(item => item.LifecycleStatus == nameof(DeviceLifecycleStatus.Active)),
            items.Count(item => item.IsBelowThreshold),
            items.Count(item => item.ReportingStatus == DeviceReportingStatus.Reporting),
            items.Count(item => item.ReportingStatus == DeviceReportingStatus.Stale),
            items.Count(item => item.ReportingStatus == DeviceReportingStatus.Offline),
            items.Count(item => item.ReportingStatus == DeviceReportingStatus.NeverReported));
    }

    public async Task<FleetPage> SearchAsync(
        FleetQuery query,
        CancellationToken cancellationToken = default,
        string? scopeDealerExternalId = null)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var now = timeProvider.GetUtcNow();
        var staleCutoff = now - monitoringSchedule.StaleAfter;
        var offlineCutoff = now - monitoringSchedule.OfflineAfter;
        var filtered =
            from installation in dbContext.DeviceInstallations.AsNoTracking()
            where installation.RemovedAtUtc == null
            from latest in dbContext.TelemetryReadings
                .Where(reading => reading.DeviceInstallationId == installation.Id
                    && reading.Quality >= TelemetryBatchValidator.MinimumOperationalQuality
                    && reading.ErrorFlagsJson == "[]")
                .OrderByDescending(reading => reading.ReceivedAtUtc)
                .ThenByDescending(reading => reading.Id)
                .Take(1)
                .DefaultIfEmpty()
            select new
            {
                installation.DeviceId,
                InstallationId = installation.Id,
                installation.Device.SerialNumber,
                installation.Device.Model,
                LifecycleStatus = installation.Device.Status,
                DealerExternalId = installation.Dealer == null ? null : installation.Dealer.ExternalId,
                DealerName = installation.Dealer == null ? "Unassigned" : installation.Dealer.DisplayName,
                CustomerDisplayName = installation.Tank.ServiceLocation.CustomerAccount.DisplayName,
                installation.Tank.ServiceLocation.CustomerAccount.AccountNumber,
                LocationDisplayName = installation.Tank.ServiceLocation.DisplayName,
                installation.Tank.ServiceLocation.AddressSummary,
                TankLabel = installation.Tank.Label,
                installation.Tank.CapacityPounds,
                SensorStatus = installation.Device.LastSensorStatus,
                SensorFault = installation.Device.LastSensorFault,
                installation.Device.LastHealthReportedAtUtc,
                installation.Device.LastHealthFirmwareVersion,
                installation.Device.LastHealthWifiRssiDbm,
                ClockSynchronized = installation.Device.LastClockSynchronized,
                QueuedReadingCount = installation.Device.LastQueuedReadingCount,
                DroppedReadingCount = installation.Device.LastDroppedReadingCount,
                LatestReceivedAtUtc = latest == null ? null : (DateTimeOffset?)latest.ReceivedAtUtc,
                LatestFillPercent = latest == null ? null : (double?)latest.FillPercent,
                LatestRawDistanceMm = latest == null ? null : (int?)latest.RawDistanceMm,
                LatestQuality = latest == null ? null : (int?)latest.Quality,
                LatestWifiRssiDbm = latest == null ? null : (int?)latest.WifiRssiDbm,
                LatestFirmwareVersion = latest == null ? null : latest.FirmwareVersion,
                LatestErrorFlagsJson = latest == null ? null : latest.ErrorFlagsJson
            };

        var filter = scopeDealerExternalId is null
            ? query.Filter
            : query.Filter with { DealerExternalId = scopeDealerExternalId };
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var escaped = filter.Search.Trim()
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal)
                .Replace("_", "\\_", StringComparison.Ordinal);
            var pattern = $"%{escaped}%";
            filtered = filtered.Where(row =>
                EF.Functions.ILike(row.DealerName, pattern, "\\")
                || EF.Functions.ILike(row.CustomerDisplayName, pattern, "\\")
                || (row.AccountNumber != null && EF.Functions.ILike(row.AccountNumber, pattern, "\\"))
                || EF.Functions.ILike(row.LocationDisplayName, pattern, "\\")
                || (row.AddressSummary != null && EF.Functions.ILike(row.AddressSummary, pattern, "\\"))
                || EF.Functions.ILike(row.TankLabel, pattern, "\\")
                || EF.Functions.ILike(row.SerialNumber, pattern, "\\"));
        }
        filtered = filter.ReportingStatus switch
        {
            DeviceReportingStatus.Reporting => filtered.Where(row => row.LatestReceivedAtUtc > staleCutoff
                && row.SensorStatus != SensorHealthStatus.Faulted),
            DeviceReportingStatus.Stale => filtered.Where(row => row.LatestReceivedAtUtc > offlineCutoff
                && (row.LatestReceivedAtUtc <= staleCutoff || row.SensorStatus == SensorHealthStatus.Faulted)),
            DeviceReportingStatus.Offline => filtered.Where(row => row.LatestReceivedAtUtc <= offlineCutoff),
            DeviceReportingStatus.NeverReported => filtered.Where(row => row.LatestReceivedAtUtc == null),
            _ => filtered
        };
        if (filter.BelowThreshold is { } belowThreshold)
        {
            filtered = belowThreshold
                ? filtered.Where(row => row.LatestFillPercent < MonitoringPolicy.LowFillThresholdPercent)
                : filtered.Where(row => row.LatestFillPercent >= MonitoringPolicy.LowFillThresholdPercent);
        }
        if (!string.IsNullOrWhiteSpace(filter.LifecycleStatus))
        {
            filtered = Enum.TryParse<DeviceLifecycleStatus>(filter.LifecycleStatus.Trim(), true, out var lifecycle)
                && Enum.IsDefined(lifecycle)
                ? filtered.Where(row => row.LifecycleStatus == lifecycle)
                : filtered.Where(_ => false);
        }
        if (!string.IsNullOrWhiteSpace(filter.FirmwareVersion))
        {
            var firmware = filter.FirmwareVersion.Trim();
            filtered = filtered.Where(row => row.LatestReceivedAtUtc != null
                && (row.LastHealthReportedAtUtc >= row.LatestReceivedAtUtc
                    ? row.LastHealthFirmwareVersion == firmware
                    : row.LatestFirmwareVersion == firmware));
        }
        if (!string.IsNullOrWhiteSpace(filter.DealerExternalId))
        {
            var dealer = filter.DealerExternalId.Trim();
            filtered = dealer.Equals("unassigned", StringComparison.OrdinalIgnoreCase)
                ? filtered.Where(row => row.DealerExternalId == null)
                : filtered.Where(row => row.DealerExternalId == dealer);
        }

        var totalCount = await filtered.CountAsync(cancellationToken);
        var ordered = query.Sort switch
        {
            FleetSort.LastReported => filtered.OrderBy(row => row.LatestReceivedAtUtc == null)
                .ThenByDescending(row => row.LatestReceivedAtUtc),
            FleetSort.FillAscending => filtered.OrderBy(row => row.LatestFillPercent == null)
                .ThenBy(row => row.LatestFillPercent),
            FleetSort.FillDescending => filtered.OrderBy(row => row.LatestFillPercent == null)
                .ThenByDescending(row => row.LatestFillPercent),
            FleetSort.Customer => filtered.OrderBy(row => row.CustomerDisplayName)
                .ThenBy(row => row.LocationDisplayName),
            _ => filtered.OrderBy(row => row.LatestReceivedAtUtc == null ? 1
                    : row.LatestReceivedAtUtc <= offlineCutoff ? 0
                    : row.SensorStatus == SensorHealthStatus.Faulted || row.LatestReceivedAtUtc <= staleCutoff ? 2
                    : row.LatestFillPercent < MonitoringPolicy.LowFillThresholdPercent ? 4 : 5)
                .ThenBy(row => row.LatestReceivedAtUtc ?? DateTimeOffset.MinValue)
                .ThenBy(row => row.CustomerDisplayName)
        };
        var rows = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);
        var items = rows.Select(row =>
        {
            var latestHealthIsNewer = row.LastHealthReportedAtUtc is { } healthAt
                && (row.LatestReceivedAtUtc is null || healthAt >= row.LatestReceivedAtUtc);
            var reportingStatus = MonitoringPolicy.GetReportingStatus(row.LatestReceivedAtUtc, now, monitoringSchedule);
            if (row.SensorStatus == SensorHealthStatus.Faulted && reportingStatus == DeviceReportingStatus.Reporting)
            {
                reportingStatus = DeviceReportingStatus.Stale;
            }
            return new FleetDeviceListItem(
                row.DeviceId, row.InstallationId, row.SerialNumber, row.Model,
                row.LifecycleStatus.ToString(), row.DealerExternalId, row.DealerName,
                row.CustomerDisplayName, row.AccountNumber, row.LocationDisplayName, row.AddressSummary,
                row.TankLabel, row.CapacityPounds, row.LatestFillPercent,
                row.LatestFillPercent is { } fill && MonitoringPolicy.IsBelowFillThreshold(fill),
                reportingStatus, row.LatestReceivedAtUtc, row.LatestRawDistanceMm, row.LatestQuality,
                latestHealthIsNewer ? row.LastHealthWifiRssiDbm ?? row.LatestWifiRssiDbm : row.LatestWifiRssiDbm,
                latestHealthIsNewer ? row.LastHealthFirmwareVersion ?? row.LatestFirmwareVersion : row.LatestFirmwareVersion,
                ParseErrorFlags(row.LatestErrorFlagsJson), row.SensorStatus, row.SensorFault,
                row.LastHealthReportedAtUtc, row.ClockSynchronized, row.QueuedReadingCount, row.DroppedReadingCount);
        }).ToArray();

        return new(now, items, totalCount, page, pageSize);
    }

    public async Task<FleetDeviceDetail?> GetDeviceAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default,
        string? scopeDealerExternalId = null)
    {
        var installation = await dbContext.DeviceInstallations
            .AsNoTracking()
            .AsSplitQuery()
            .Where(candidate => candidate.DeviceId == deviceId)
            .OrderBy(candidate => candidate.RemovedAtUtc == null ? 0 : 1)
            .ThenByDescending(candidate => candidate.InstalledAtUtc)
            .Include(candidate => candidate.Device)
                .ThenInclude(device => device.Credentials)
            .Include(candidate => candidate.Dealer)
            .Include(candidate => candidate.Tank)
                .ThenInclude(tank => tank.ServiceLocation)
                    .ThenInclude(location => location.CustomerAccount)
            .Include(candidate => candidate.Calibrations)
            .FirstOrDefaultAsync(cancellationToken);

        if (installation is null)
        {
            return null;
        }

        if (scopeDealerExternalId is not null
            && !string.Equals(installation.Dealer?.ExternalId, scopeDealerExternalId, StringComparison.Ordinal))
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var latestReading = await GetLatestReadingAsync(installation.Id, cancellationToken);
        var item = MapItem(installation, latestReading, now);
        var calibration = installation.Calibrations
            .OrderBy(candidate => candidate.EffectiveToUtc == null ? 0 : 1)
            .ThenByDescending(candidate => candidate.Version)
            .FirstOrDefault();
        var activeCredentials = installation.Device.Credentials
            .Where(credential => credential.RevokedAtUtc == null
                && credential.ValidFromUtc <= now
                && (credential.ExpiresAtUtc == null || credential.ExpiresAtUtc > now))
            .ToArray();

        return new(
            item,
            installation.Device.RegisteredAtUtc,
            installation.Device.CommissionedAtUtc,
            installation.InstalledAtUtc,
            installation.InstalledBy,
            installation.WaterFlexWorkOrderId,
            calibration?.Version,
            calibration?.TankDepthMm,
            calibration?.CommissioningDistanceMm,
            calibration?.EffectiveFromUtc,
            activeCredentials.Length > 0,
            activeCredentials.Max(credential => credential.LastUsedAtUtc),
            installation.RowVersion.ToString(CultureInfo.InvariantCulture));
    }

    public async Task<IReadOnlyList<FleetReadingPoint>?> GetReadingsAsync(
        Guid deviceId,
        TimeSpan range,
        int limit,
        CancellationToken cancellationToken = default,
        string? scopeDealerExternalId = null)
    {
        if (!await DeviceIsInScopeAsync(deviceId, scopeDealerExternalId, cancellationToken))
        {
            return null;
        }

        var cutoff = timeProvider.GetUtcNow() - range;
        var readings = await dbContext.TelemetryReadings
            .AsNoTracking()
            .Where(reading => reading.DeviceId == deviceId
                && reading.ReceivedAtUtc >= cutoff
                && reading.Quality >= TelemetryBatchValidator.MinimumOperationalQuality
                && reading.ErrorFlagsJson == "[]")
            .OrderByDescending(reading => reading.ReceivedAtUtc)
            .ThenByDescending(reading => reading.Id)
            .Take(limit)
            .Select(reading => new
            {
                reading.Id,
                reading.ObservedAtUtc,
                reading.ReceivedAtUtc,
                reading.FillPercent,
                reading.RawDistanceMm,
                reading.Quality,
                reading.WifiRssiDbm,
                reading.FirmwareVersion,
                reading.ErrorFlagsJson
            })
            .ToArrayAsync(cancellationToken);

        return readings
            .Reverse()
            .Select(reading => new FleetReadingPoint(
                reading.Id,
                reading.ObservedAtUtc ?? reading.ReceivedAtUtc,
                reading.ObservedAtUtc is not null,
                reading.ReceivedAtUtc,
                reading.FillPercent,
                reading.RawDistanceMm,
                reading.Quality,
                reading.WifiRssiDbm,
                reading.FirmwareVersion,
                ParseErrorFlags(reading.ErrorFlagsJson)))
            .ToArray();
    }

    public async Task<FleetHistory?> GetHistoryAsync(
        Guid deviceId,
        DateTimeOffset fromUtc,
        TelemetryHistoryResolution resolution,
        CancellationToken cancellationToken = default,
        string? scopeDealerExternalId = null)
    {
        if (!await DeviceIsInScopeAsync(deviceId, scopeDealerExternalId, cancellationToken))
        {
            return null;
        }

        var throughUtc = TruncateToBucket(timeProvider.GetUtcNow(), resolution);
        IReadOnlyList<FleetHistoryPoint> points = resolution switch
        {
            TelemetryHistoryResolution.Hour => await dbContext.TelemetryHourlySummaries
                .AsNoTracking()
                .Where(summary => summary.DeviceId == deviceId
                    && summary.BucketStartUtc >= fromUtc
                    && summary.BucketStartUtc < throughUtc)
                .OrderBy(summary => summary.BucketStartUtc)
                .Take(1200)
                .Select(summary => new FleetHistoryPoint(
                    summary.BucketStartUtc,
                    summary.BucketStartUtc.AddHours(1),
                    summary.LastReadingAtUtc,
                    summary.ReadingCount,
                    summary.FillPercentMin,
                    summary.FillPercentMax,
                    summary.FillPercentAverage,
                    summary.FillPercentLatest,
                    summary.RawDistanceMmMin,
                    summary.RawDistanceMmMax,
                    summary.RawDistanceMmAverage,
                    summary.WifiRssiDbmMin,
                    summary.WifiRssiDbmMax,
                    summary.WifiRssiDbmAverage,
                    summary.WorstQuality,
                    summary.ErrorCount,
                    summary.LatestFirmwareVersion))
                .ToArrayAsync(cancellationToken),
            TelemetryHistoryResolution.Day => await dbContext.TelemetryDailySummaries
                .AsNoTracking()
                .Where(summary => summary.DeviceId == deviceId
                    && summary.BucketStartUtc >= fromUtc
                    && summary.BucketStartUtc < throughUtc)
                .OrderBy(summary => summary.BucketStartUtc)
                .Take(1200)
                .Select(summary => new FleetHistoryPoint(
                    summary.BucketStartUtc,
                    summary.BucketStartUtc.AddDays(1),
                    summary.LastReadingAtUtc,
                    summary.ReadingCount,
                    summary.FillPercentMin,
                    summary.FillPercentMax,
                    summary.FillPercentAverage,
                    summary.FillPercentLatest,
                    summary.RawDistanceMmMin,
                    summary.RawDistanceMmMax,
                    summary.RawDistanceMmAverage,
                    summary.WifiRssiDbmMin,
                    summary.WifiRssiDbmMax,
                    summary.WifiRssiDbmAverage,
                    summary.WorstQuality,
                    summary.ErrorCount,
                    summary.LatestFirmwareVersion))
                .ToArrayAsync(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(resolution))
        };

        return new(resolution, fromUtc, throughUtc, points);
    }

    private async Task<bool> DeviceIsInScopeAsync(
        Guid deviceId,
        string? scopeDealerExternalId,
        CancellationToken cancellationToken) =>
        await dbContext.Devices
            .AsNoTracking()
            .Where(device => device.Id == deviceId)
            .Where(device => scopeDealerExternalId == null
                || device.Installations.Any(installation => installation.Dealer != null
                    && installation.Dealer.ExternalId == scopeDealerExternalId))
            .AnyAsync(cancellationToken);

    private static DateTimeOffset TruncateToBucket(
        DateTimeOffset value,
        TelemetryHistoryResolution resolution)
    {
        var utc = value.UtcDateTime;
        return resolution == TelemetryHistoryResolution.Hour
            ? new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero)
            : new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);
    }

    private async Task<IReadOnlyList<FleetDeviceListItem>> LoadFleetAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var installations = await dbContext.DeviceInstallations
            .AsNoTracking()
            .Where(installation => installation.RemovedAtUtc == null)
            .Include(installation => installation.Device)
            .Include(installation => installation.Dealer)
            .Include(installation => installation.Tank)
                .ThenInclude(tank => tank.ServiceLocation)
                    .ThenInclude(location => location.CustomerAccount)
            .ToArrayAsync(cancellationToken);
        var installationIds = installations.Select(installation => installation.Id).ToArray();

        if (installationIds.Length == 0)
        {
            return [];
        }

        var latestReadings = await dbContext.TelemetryReadings
            .AsNoTracking()
            .Where(reading => installationIds.Contains(reading.DeviceInstallationId))
            .Where(reading => reading.Quality >= TelemetryBatchValidator.MinimumOperationalQuality
                && reading.ErrorFlagsJson == "[]")
            .Where(reading => reading.Id == dbContext.TelemetryReadings
                .Where(candidate => candidate.DeviceInstallationId == reading.DeviceInstallationId
                    && candidate.Quality >= TelemetryBatchValidator.MinimumOperationalQuality
                    && candidate.ErrorFlagsJson == "[]")
                .OrderByDescending(candidate => candidate.ReceivedAtUtc)
                .ThenByDescending(candidate => candidate.Id)
                .Select(candidate => candidate.Id)
                .First())
            .ToDictionaryAsync(reading => reading.DeviceInstallationId, cancellationToken);

        return installations
            .Select(installation => MapItem(
                installation,
                latestReadings.GetValueOrDefault(installation.Id),
                now))
            .ToArray();
    }

    private async Task<TelemetryReadingRecord?> GetLatestReadingAsync(
        Guid installationId,
        CancellationToken cancellationToken) =>
        await dbContext.TelemetryReadings
            .AsNoTracking()
            .Where(reading => reading.DeviceInstallationId == installationId
                && reading.Quality >= TelemetryBatchValidator.MinimumOperationalQuality
                && reading.ErrorFlagsJson == "[]")
            .OrderByDescending(reading => reading.ReceivedAtUtc)
            .ThenByDescending(reading => reading.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private FleetDeviceListItem MapItem(
        DeviceInstallation installation,
        TelemetryReadingRecord? latestReading,
        DateTimeOffset now)
    {
        var location = installation.Tank.ServiceLocation;
        var customer = location.CustomerAccount;
        var fillPercent = latestReading?.FillPercent;
        var latestHealthIsNewer = installation.Device.LastHealthReportedAtUtc is { } healthAt
            && (latestReading is null || healthAt >= latestReading.ReceivedAtUtc);
        var wifiRssiDbm = latestHealthIsNewer
            ? installation.Device.LastHealthWifiRssiDbm ?? latestReading?.WifiRssiDbm
            : latestReading?.WifiRssiDbm;
        var firmwareVersion = latestHealthIsNewer
            ? installation.Device.LastHealthFirmwareVersion ?? latestReading?.FirmwareVersion
            : latestReading?.FirmwareVersion;
        var reportingStatus = MonitoringPolicy.GetReportingStatus(
            latestReading?.ReceivedAtUtc,
            now,
            monitoringSchedule);
        if (installation.Device.LastSensorStatus == SensorHealthStatus.Faulted
            && reportingStatus == DeviceReportingStatus.Reporting)
        {
            reportingStatus = DeviceReportingStatus.Stale;
        }

        return new(
            installation.DeviceId,
            installation.Id,
            installation.Device.SerialNumber,
            installation.Device.Model,
            installation.Device.Status.ToString(),
            installation.Dealer?.ExternalId,
            installation.Dealer?.DisplayName ?? "Unassigned",
            customer.DisplayName,
            customer.AccountNumber,
            location.DisplayName,
            location.AddressSummary,
            installation.Tank.Label,
            installation.Tank.CapacityPounds,
            fillPercent,
            fillPercent is { } value && MonitoringPolicy.IsBelowFillThreshold(value),
            reportingStatus,
            latestReading?.ReceivedAtUtc,
            latestReading?.RawDistanceMm,
            latestReading?.Quality,
            wifiRssiDbm,
            firmwareVersion,
            ParseErrorFlags(latestReading?.ErrorFlagsJson),
            installation.Device.LastSensorStatus,
            installation.Device.LastSensorFault,
            installation.Device.LastHealthReportedAtUtc,
            installation.Device.LastClockSynchronized,
            installation.Device.LastQueuedReadingCount,
            installation.Device.LastDroppedReadingCount);
    }

    private static IEnumerable<FleetDeviceListItem> ApplyFilter(
        IEnumerable<FleetDeviceListItem> items,
        FleetFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            items = items.Where(item =>
                Contains(item.DealerName, term)
                || Contains(item.CustomerDisplayName, term)
                || Contains(item.AccountNumber, term)
                || Contains(item.LocationDisplayName, term)
                || Contains(item.AddressSummary, term)
                || Contains(item.TankLabel, term)
                || Contains(item.SerialNumber, term));
        }

        if (filter.ReportingStatus is { } reportingStatus)
        {
            items = items.Where(item => item.ReportingStatus == reportingStatus);
        }

        if (filter.BelowThreshold is { } belowThreshold)
        {
            items = items.Where(item => item.FillPercent is not null
                && item.IsBelowThreshold == belowThreshold);
        }

        if (!string.IsNullOrWhiteSpace(filter.LifecycleStatus))
        {
            items = items.Where(item => item.LifecycleStatus.Equals(
                filter.LifecycleStatus.Trim(),
                StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(filter.FirmwareVersion))
        {
            items = items.Where(item => string.Equals(
                item.FirmwareVersion,
                filter.FirmwareVersion.Trim(),
                StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(filter.DealerExternalId))
        {
            var dealerExternalId = filter.DealerExternalId.Trim();
            items = dealerExternalId.Equals("unassigned", StringComparison.OrdinalIgnoreCase)
                ? items.Where(item => item.DealerExternalId is null)
                : items.Where(item => string.Equals(
                    item.DealerExternalId,
                    dealerExternalId,
                    StringComparison.OrdinalIgnoreCase));
        }

        return items;
    }

    private static IOrderedEnumerable<FleetDeviceListItem> ApplySort(
        IEnumerable<FleetDeviceListItem> items,
        FleetSort sort) => sort switch
        {
            FleetSort.LastReported => items
                .OrderBy(item => item.LastReportedAtUtc is null)
                .ThenByDescending(item => item.LastReportedAtUtc),
            FleetSort.FillAscending => items
                .OrderBy(item => item.FillPercent is null)
                .ThenBy(item => item.FillPercent),
            FleetSort.FillDescending => items
                .OrderBy(item => item.FillPercent is null)
                .ThenByDescending(item => item.FillPercent),
            FleetSort.Customer => items
                .OrderBy(item => item.CustomerDisplayName)
                .ThenBy(item => item.LocationDisplayName),
            _ => items
                .OrderBy(GetAttentionRank)
                .ThenBy(item => item.LastReportedAtUtc ?? DateTimeOffset.MinValue)
                .ThenBy(item => item.CustomerDisplayName)
        };

    private static int GetAttentionRank(FleetDeviceListItem item) => item.ReportingStatus switch
    {
        DeviceReportingStatus.Offline => 0,
        DeviceReportingStatus.NeverReported => 1,
        _ when item.SensorStatus == SensorHealthStatus.Faulted => 2,
        DeviceReportingStatus.Stale => 2,
        _ when item.ErrorFlags.Count > 0 => 3,
        _ when item.IsBelowThreshold => 4,
        _ => 5
    };

    private static IReadOnlyList<string> ParseErrorFlags(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return ["invalid_error_flags"];
        }
    }

    private static bool Contains(string? value, string term) =>
        value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
}
