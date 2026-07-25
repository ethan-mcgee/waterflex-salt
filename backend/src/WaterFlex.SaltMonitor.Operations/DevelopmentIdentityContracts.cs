using WaterFlex.SaltMonitor.Domain.Security;

namespace WaterFlex.SaltMonitor.Operations;

public interface IDevelopmentIdentityDirectory
{
    IReadOnlyList<StaffActor> GetUsers();
    StaffActor? Resolve(string userId);
}