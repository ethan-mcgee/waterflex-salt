using WaterFlex.SaltMonitor.Domain.Security;
using WaterFlex.SaltMonitor.Operations;

namespace WaterFlex.SaltMonitor.Api;

public static class DevelopmentIdentity
{
    public const string HeaderName = "X-WaterFlex-Development-User";
    private const string ActorItemKey = "WaterFlex.DevelopmentActor";

    public static RouteGroupBuilder RequireDevelopmentRole(
        this RouteGroupBuilder group,
        StaffRole requiredRole)
    {
        group.AddEndpointFilter(async (context, next) =>
        {
            var httpContext = context.HttpContext;
            var userId = httpContext.Request.Headers[HeaderName].ToString();
            var directory = httpContext.RequestServices
                .GetRequiredService<IDevelopmentIdentityDirectory>();
            var actor = string.IsNullOrWhiteSpace(userId) ? null : directory.Resolve(userId);

            if (actor is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Development identity required",
                    detail: $"Select a development user or send the {HeaderName} header.");
            }

            if (actor.Role != requiredRole)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Development role not permitted");
            }

            httpContext.Items[ActorItemKey] = actor;
            return await next(context);
        });

        return group;
    }

    public static StaffActor GetDevelopmentActor(this HttpContext context) =>
        context.Items.TryGetValue(ActorItemKey, out var value) && value is StaffActor actor
            ? actor
            : throw new InvalidOperationException("Development actor was not resolved for this endpoint.");
}