using WaterFlex.SaltMonitor.Domain.Security;
using WaterFlex.SaltMonitor.Operations;

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence;

public sealed class DevelopmentIdentityDirectory : IDevelopmentIdentityDirectory
{
    private static readonly IReadOnlyList<StaffActor> Users =
    [
        new("wf-ops-alex", "Alex Morgan", StaffRole.WaterFlexEmployee, null, null),
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