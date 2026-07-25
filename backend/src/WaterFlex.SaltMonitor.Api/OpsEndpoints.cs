using WaterFlex.SaltMonitor.Domain.Monitoring;
using WaterFlex.SaltMonitor.Domain.Security;
using WaterFlex.SaltMonitor.Operations;

namespace WaterFlex.SaltMonitor.Api;

public static class OpsEndpoints
{
    public static IEndpointRouteBuilder MapOpsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var opsApi = endpoints.MapGroup("/api/v1/ops")
            .WithTags("Internal operations")
            .RequireDevelopmentRole(StaffRole.WaterFlexEmployee);

        opsApi.MapGet("/dealers", async (
                IFleetQueryService fleetQueryService,
                CancellationToken cancellationToken) =>
            Results.Ok(await fleetQueryService.GetDealersAsync(cancellationToken)))
            .WithName("GetFleetDealers")
            .WithSummary("List dealers represented in the sensor fleet");

        opsApi.MapGet("/fleet/summary", async (
                string? search,
                string? reportingStatus,
                bool? belowThreshold,
                string? lifecycleStatus,
                string? firmwareVersion,
                string? dealerId,
                IFleetQueryService fleetQueryService,
                CancellationToken cancellationToken) =>
            {
                if (!TryParseOptionalEnum<DeviceReportingStatus>(reportingStatus, out var parsedStatus))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["reportingStatus"] = ["Reporting status is not recognized."]
                    });
                }

                return Results.Ok(await fleetQueryService.GetSummaryAsync(
                    CreateFilter(
                        search,
                        parsedStatus,
                        belowThreshold,
                        lifecycleStatus,
                        firmwareVersion,
                        dealerId),
                    cancellationToken));
            })
            .WithName("GetFleetSummary")
            .WithSummary("Get sensor fleet summary");

        opsApi.MapGet("/devices", async (
                string? search,
            string? reportingStatus,
                bool? belowThreshold,
                string? lifecycleStatus,
                string? firmwareVersion,
                string? dealerId,
                string? sort,
                int? page,
                int? pageSize,
                IFleetQueryService fleetQueryService,
                CancellationToken cancellationToken) =>
            {
                if (page is < 1 || pageSize is < 1 or > 100)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["paging"] = ["Page must be at least 1 and page size must be between 1 and 100."]
                    });
                }

                if (!TryParseOptionalEnum<DeviceReportingStatus>(reportingStatus, out var parsedStatus)
                    || !TryParseOptionalEnum<FleetSort>(sort, out var parsedSort))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["filters"] = ["Reporting status or sort value is not recognized."]
                    });
                }

                var result = await fleetQueryService.SearchAsync(
                    new(
                        CreateFilter(
                            search,
                            parsedStatus,
                            belowThreshold,
                            lifecycleStatus,
                            firmwareVersion,
                            dealerId),
                        parsedSort ?? FleetSort.Attention,
                        page ?? 1,
                        pageSize ?? 50),
                    cancellationToken);
                return Results.Ok(result);
            })
            .WithName("SearchFleetDevices")
            .WithSummary("Search provisioned sensors");

        opsApi.MapGet("/devices/{deviceId:guid}", async (
                Guid deviceId,
                IFleetQueryService fleetQueryService,
                CancellationToken cancellationToken) =>
            await fleetQueryService.GetDeviceAsync(deviceId, cancellationToken) is { } device
                ? Results.Ok(device)
                : Results.NotFound())
            .WithName("GetFleetDevice")
            .WithSummary("Get sensor operations detail");

        opsApi.MapGet("/devices/{deviceId:guid}/readings", async (
                Guid deviceId,
                string? range,
                IFleetQueryService fleetQueryService,
                CancellationToken cancellationToken) =>
            {
                if (!TryParseRange(range, out var duration))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["range"] = ["Range must be one of 24h, 7d, or 30d."]
                    });
                }

                return await fleetQueryService.GetReadingsAsync(deviceId, duration, cancellationToken) is { } readings
                    ? Results.Ok(readings)
                    : Results.NotFound();
            })
            .WithName("GetFleetDeviceReadings")
            .WithSummary("Get bounded sensor reading history");

        return endpoints;
    }

    private static FleetFilter CreateFilter(
        string? search,
        DeviceReportingStatus? reportingStatus,
        bool? belowThreshold,
        string? lifecycleStatus,
        string? firmwareVersion,
        string? dealerId) =>
        new(search, reportingStatus, belowThreshold, lifecycleStatus, firmwareVersion, dealerId);

    private static bool TryParseRange(string? value, out TimeSpan range)
    {
        range = value?.ToLowerInvariant() switch
        {
            null or "24h" => TimeSpan.FromHours(24),
            "7d" => TimeSpan.FromDays(7),
            "30d" => TimeSpan.FromDays(30),
            _ => TimeSpan.Zero
        };
        return range > TimeSpan.Zero;
    }

    private static bool TryParseOptionalEnum<TEnum>(string? value, out TEnum? result)
        where TEnum : struct, Enum
    {
        result = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            || !Enum.IsDefined(typeof(TEnum), parsed))
        {
            return false;
        }

        result = parsed;
        return true;
    }
}