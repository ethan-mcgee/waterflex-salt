using WaterFlex.SaltMonitor.Domain.Security;
using WaterFlex.SaltMonitor.Operations;

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence;

/// <summary>
/// Hardcoded staff identities for local development and demos, standing in for the real
/// identity-provider-backed staff directory until a caller authenticates against Cognito.
/// </summary>
public sealed class DevelopmentIdentityDirectory : IDevelopmentIdentityDirectory
{
    private static readonly IReadOnlyList<StaffActor> Users =
    [
        new("wf-admin-avery", "Avery Patel", StaffRole.WaterFlexAdministrator, null, null),
        new("wf-ops-alex", "Alex Morgan", StaffRole.WaterFlexEmployee, null, null),
        new(
            "north-star-admin-taylor",
            "Taylor Brooks",
            StaffRole.DealerAdministrator,
            "WF-D-NORTH-STAR",
            "North Star Water Systems"),
        new(
            "north-star-jordan",
            "Jordan Lee",
            StaffRole.DealerTechnician,
            "WF-D-NORTH-STAR",
            "North Star Water Systems"),
        new(
            "lakes-water-sam",
            "Sam Rivera",
            StaffRole.DealerTechnician,
            "WF-D-LAKES-WATER",
            "Lakes Water Conditioning")
    ];

    public IReadOnlyList<StaffActor> GetUsers() => Users;

    public StaffActor? Resolve(string userId) => Users.SingleOrDefault(user =>
        user.UserId.Equals(userId.Trim(), StringComparison.OrdinalIgnoreCase));
}
