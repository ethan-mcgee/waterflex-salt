using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WaterFlex.SaltMonitor.Domain.Monitoring;
using WaterFlex.SaltMonitor.Operations;

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence;

public sealed class EfFleetQueryService(
    SaltMonitorDbContext dbContext,
    TimeProvider timeProvider,
    MonitoringSchedule monitoringSchedule) : IFleetQueryService
{
    public async Task<IReadOnlyList<FleetDealerOption>> GetDealersAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.Dealers
            .AsNoTracking()
            .Where(dealer => dealer.IsActive)
            .OrderBy(dealer => dealer.DisplayName)
            .Select(dealer => new FleetDealerOption(dealer.ExternalId, dealer.DisplayName))
            .ToArrayAsync(cancellationToken);

    public async Task<FleetSummary> GetSummaryAsync(
        FleetFilter filter,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var items = ApplyFilter(await LoadFleetAsync(now, cancellationToken), filter).ToArray();

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
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var now = timeProvider.GetUtcNow();
        var filtered = ApplyFilter(await LoadFleetAsync(now, cancellationToken), query.Filter);
        var ordered = ApplySort(filtered, query.Sort);
        var totalCount = ordered.Count();
        var items = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArray();

        return new(now, items, totalCount, page, pageSize);
    }

    public async Task<FleetDeviceDetail?> GetDeviceAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
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
            Convert.ToBase64String(installation.RowVersion));
    }

    public async Task<IReadOnlyList<FleetReadingPoint>?> GetReadingsAsync(
        Guid deviceId,
        TimeSpan range,
        CancellationToken cancellationToken = default)
    {
        if (!await dbContext.Devices.AsNoTracking().AnyAsync(
            device => device.Id == deviceId,
            cancellationToken))
        {
            return null;
        }

        var cutoff = timeProvider.GetUtcNow() - range;
        var readings = await dbContext.TelemetryReadings
            .AsNoTracking()
            .Where(reading => reading.DeviceId == deviceId && reading.ReceivedAtUtc >= cutoff)
            .OrderByDescending(reading => reading.ReceivedAtUtc)
            .ThenByDescending(reading => reading.Id)
            .Take(2000)
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
            .Where(reading => reading.Id == dbContext.TelemetryReadings
                .Where(candidate => candidate.DeviceInstallationId == reading.DeviceInstallationId)
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
            .Where(reading => reading.DeviceInstallationId == installationId)
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

        return new(
            installation.DeviceId,
            installation.Id,
            installation.Device.SerialNumber,
            installation.Device.HardwareId,
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
            MonitoringPolicy.GetReportingStatus(
                latestReading?.ReceivedAtUtc,
                now,
                monitoringSchedule),
            latestReading?.ReceivedAtUtc,
            latestReading?.RawDistanceMm,
            latestReading?.Quality,
            latestReading?.WifiRssiDbm,
            latestReading?.FirmwareVersion,
            ParseErrorFlags(latestReading?.ErrorFlagsJson));
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
                || Contains(item.SerialNumber, term)
                || Contains(item.HardwareId, term));
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