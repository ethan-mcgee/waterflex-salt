using WaterFlex.SaltMonitor.Domain.Security;

namespace WaterFlex.SaltMonitor.Operations;

/// <summary>
/// Local, in-memory roster of staff identities used to simulate Cloudflare Access sign-in during
/// development, when there is no real identity provider to authenticate against.
/// </summary>
public interface IDevelopmentIdentityDirectory
{
    IReadOnlyList<StaffActor> GetUsers();
    StaffActor? Resolve(string userId);
}