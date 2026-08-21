using WaterFlex.SaltMonitor.Domain.Security;
using WaterFlex.SaltMonitor.Operations;

namespace WaterFlex.SaltMonitor.Api;

public static class DevelopmentIdentity
{
    public const string HeaderName = "X-WaterFlex-Development-User";
    public const string AuthenticatedPolicy = "Staff:Authenticated";
    public const string ActivationPolicy = "Staff:ActivationCandidate";

    public static string PolicyName(StaffRole role) => $"Staff:{role}";
    public static string CapabilityPolicyName(StaffCapability capability) => $"StaffCapability:{capability}";

    public static RouteGroupBuilder RequireStaffRole(
        this RouteGroupBuilder group,
        StaffRole requiredRole) => group.RequireAuthorization(PolicyName(requiredRole));

    public static RouteGroupBuilder RequireStaffCapability(
        this RouteGroupBuilder group,
        StaffCapability capability) => group.RequireAuthorization(CapabilityPolicyName(capability));

    public static StaffActor GetStaffActor(this HttpContext context)
    {
        var user = context.User;
        var userId = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var displayName = user.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
        var roleValue = user.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (string.IsNullOrWhiteSpace(userId)
            || string.IsNullOrWhiteSpace(displayName)
            || !Enum.TryParse<StaffRole>(roleValue, out var role))
        {
            throw new InvalidOperationException("Authenticated staff actor was not resolved for this endpoint.");
        }

        return new StaffActor(
            userId,
            displayName,
            role,
            user.FindFirst("dealer_external_id")?.Value,
            user.FindFirst("dealer_name")?.Value);
    }
}
