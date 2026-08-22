using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.RateLimiting;
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
            .RequireRateLimiting(RateLimitPolicies.Staff)
            .RequireStaffCapability(StaffCapability.FleetOperations);

        opsApi.MapGet("/alerts", async (
                string? status,
                int? page,
                int? pageSize,
                HttpContext httpContext,
                IAlertOperationsService alerts,
                CancellationToken cancellationToken) =>
            {
                if (!TryParseOptionalEnum<LowSaltAlertStatus>(status, out var parsedStatus)
                    || page is < 1
                    || pageSize is < 1 or > 100)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["query"] = ["Status must be recognized, page must be positive, and pageSize must be between 1 and 100."]
                    });
                }
                return Results.Ok(await alerts.SearchAsync(
                    parsedStatus,
                    page ?? 1,
                    pageSize ?? 50,
                    cancellationToken,
                    DealerScope(httpContext.GetStaffActor())));
            })
            .WithName("GetLowSaltAlerts")
            .WithSummary("List reviewable low-salt alerts");

        opsApi.MapGet("/alerts/{alertId:guid}", async (
                Guid alertId,
                HttpContext httpContext,
                IAlertOperationsService alerts,
                CancellationToken cancellationToken) =>
            await alerts.GetAsync(alertId, cancellationToken, DealerScope(httpContext.GetStaffActor())) is { } alert
                ? Results.Ok(alert)
                : Results.NotFound())
            .WithName("GetLowSaltAlert")
            .WithSummary("Get an alert and its audit history");

        MapAlertTransition(opsApi, "acknowledge", AlertTransition.Acknowledge);
        MapAlertTransition(opsApi, "approve", AlertTransition.Approve);
        MapAlertTransition(opsApi, "dismiss", AlertTransition.Dismiss);

        opsApi.MapGet("/dealers", async (
                HttpContext httpContext,
                IFleetQueryService fleetQueryService,
                CancellationToken cancellationToken) =>
            Results.Ok(await fleetQueryService.GetDealersAsync(
                cancellationToken,
                DealerScope(httpContext.GetStaffActor()))))
            .WithName("GetFleetDealers")
            .WithSummary("List dealers represented in the sensor fleet");

        opsApi.MapGet("/fleet/summary", async (
                string? search,
                string? reportingStatus,
                bool? belowThreshold,
                string? lifecycleStatus,
                string? firmwareVersion,
                string? dealerId,
                HttpContext httpContext,
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
                    cancellationToken,
                    DealerScope(httpContext.GetStaffActor())));
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
                HttpContext httpContext,
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
                    cancellationToken,
                    DealerScope(httpContext.GetStaffActor()));
                return Results.Ok(result);
            })
            .WithName("SearchFleetDevices")
            .WithSummary("Search provisioned sensors");

        opsApi.MapGet("/devices/{deviceId:guid}", async (
                Guid deviceId,
                HttpContext httpContext,
                IFleetQueryService fleetQueryService,
                CancellationToken cancellationToken) =>
            await fleetQueryService.GetDeviceAsync(deviceId, cancellationToken, DealerScope(httpContext.GetStaffActor())) is { } device
                ? Results.Ok(device)
                : Results.NotFound())
            .WithName("GetFleetDevice")
            .WithSummary("Get sensor operations detail");

        opsApi.MapGet("/devices/{deviceId:guid}/readings", async (
                Guid deviceId,
                string? range,
                int? limit,
                HttpContext httpContext,
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

                if (limit is < 1 or > 1500)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["limit"] = ["Limit must be between 1 and 1500."]
                    });
                }

                return await fleetQueryService.GetReadingsAsync(
                    deviceId, duration, limit ?? 1500, cancellationToken, DealerScope(httpContext.GetStaffActor())) is { } readings
                    ? Results.Ok(readings)
                    : Results.NotFound();
            })
            .WithName("GetFleetDeviceReadings")
            .WithSummary("Get bounded sensor reading history");

        opsApi.MapGet("/devices/{deviceId:guid}/history", async (
                Guid deviceId,
                string? range,
                string? resolution,
                HttpContext httpContext,
                IFleetQueryService fleetQueryService,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                var now = timeProvider.GetUtcNow();
                if (!TryParseHistoryRange(range, now, out var fromUtc, out var defaultResolution))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["range"] = ["Range must be one of 24h, 7d, 30d, 13m, or 3y."]
                    });
                }

                if (!TryParseHistoryResolution(resolution, defaultResolution, out var parsedResolution))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["resolution"] = ["Resolution must be one of auto, hour, or day."]
                    });
                }

                if (parsedResolution == TelemetryHistoryResolution.Hour && fromUtc < now.AddDays(-31))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["resolution"] = ["Hourly resolution is limited to ranges of 30 days or less."]
                    });
                }

                fromUtc = TruncateHistoryStart(fromUtc, parsedResolution);

                var history = await fleetQueryService.GetHistoryAsync(
                    deviceId,
                    fromUtc,
                    parsedResolution,
                    cancellationToken,
                    DealerScope(httpContext.GetStaffActor()));
                if (history is null)
                {
                    return Results.NotFound();
                }

                var entityTag = CreateHistoryEntityTag(history);
                httpContext.Response.Headers.ETag = entityTag;
                httpContext.Response.Headers.CacheControl = "private, max-age=60, must-revalidate";
                httpContext.Response.Headers.Vary = "Accept-Encoding, X-WaterFlex-Development-User";
                if (httpContext.Request.Headers.IfNoneMatch.Any(value => value == entityTag))
                {
                    return Results.StatusCode(StatusCodes.Status304NotModified);
                }

                return Results.Ok(history);
            })
            .WithName("GetFleetDeviceHistory")
            .WithSummary("Get hourly or daily sensor history summaries");

        return endpoints;
    }

    private static void MapAlertTransition(
        RouteGroupBuilder group,
        string route,
        AlertTransition transition)
    {
        group.MapPost($"/alerts/{{alertId:guid}}/{route}", async (
                Guid alertId,
                AlertTransitionRequest request,
                HttpContext httpContext,
                IAlertOperationsService alerts,
                CancellationToken cancellationToken) =>
            {
                var actor = httpContext.GetStaffActor();
                var result = await alerts.TransitionAsync(
                    alertId,
                    transition,
                    request,
                    actor,
                    cancellationToken,
                    DealerScope(actor));
                return result.Failure switch
                {
                    AlertTransitionFailure.None => Results.Ok(result.Alert),
                    AlertTransitionFailure.NotFound => Results.NotFound(),
                    AlertTransitionFailure.InvalidRequest => Results.ValidationProblem(
                        new Dictionary<string, string[]> { ["request"] = ["Expected row version and a valid dismissal reason are required."] }),
                    AlertTransitionFailure.InvalidState => Results.Conflict(new { errorCode = "invalid_alert_state" }),
                    AlertTransitionFailure.Conflict => Results.Conflict(new { errorCode = "alert_version_conflict" }),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                };
            })
            .WithName($"{transition}LowSaltAlert")
            .WithSummary($"{transition} a low-salt alert");
    }

    // A dealer administrator only sees their own dealer's sensors and alerts; WaterFlex staff see the whole fleet.
    private static string? DealerScope(StaffActor actor) =>
        actor.Role == StaffRole.DealerAdministrator ? actor.DealerExternalId : null;

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

    private static bool TryParseHistoryRange(
        string? value,
        DateTimeOffset now,
        out DateTimeOffset fromUtc,
        out TelemetryHistoryResolution defaultResolution)
    {
        defaultResolution = TelemetryHistoryResolution.Hour;
        fromUtc = value?.ToLowerInvariant() switch
        {
            null or "7d" => now.AddDays(-7),
            "24h" => now.AddHours(-24),
            "30d" => now.AddDays(-30),
            "13m" => now.AddMonths(-13),
            "3y" => now.AddYears(-3),
            _ => DateTimeOffset.MaxValue
        };
        if (value?.ToLowerInvariant() is "13m" or "3y")
        {
            defaultResolution = TelemetryHistoryResolution.Day;
        }
        return fromUtc != DateTimeOffset.MaxValue;
    }

    private static bool TryParseHistoryResolution(
        string? value,
        TelemetryHistoryResolution defaultResolution,
        out TelemetryHistoryResolution resolution)
    {
        resolution = value?.ToLowerInvariant() switch
        {
            null or "auto" => defaultResolution,
            "hour" => TelemetryHistoryResolution.Hour,
            "day" => TelemetryHistoryResolution.Day,
            _ => (TelemetryHistoryResolution)(-1)
        };
        return Enum.IsDefined(resolution);
    }

    private static string CreateHistoryEntityTag(FleetHistory history)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(history);
        var hash = Convert.ToHexString(SHA256.HashData(payload));
        return $"W/\"{hash[..24]}\"";
    }

    private static DateTimeOffset TruncateHistoryStart(
        DateTimeOffset value,
        TelemetryHistoryResolution resolution)
    {
        var utc = value.UtcDateTime;
        return resolution == TelemetryHistoryResolution.Hour
            ? new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero)
            : new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);
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
            || !Enum.IsDefined(parsed))
        {
            return false;
        }

        result = parsed;
        return true;
    }
}
