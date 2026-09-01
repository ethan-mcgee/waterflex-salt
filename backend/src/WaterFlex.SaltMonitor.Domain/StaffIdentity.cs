namespace WaterFlex.SaltMonitor.Domain.Security;

/// <summary>The role a staff member holds, distinguishing dealer-side technicians/admins from internal WaterFlex staff.</summary>
public enum StaffRole
{
    DealerTechnician,
    DealerAdministrator,
    FactoryWorker,
    WaterFlexEmployee,
    WaterFlexAdministrator
}

/// <summary>A discrete permission a staff member may or may not hold, granted per <see cref="StaffRole"/> via <see cref="StaffRoleCapabilities"/>.</summary>
public enum StaffCapability
{
    StaffAdministration,
    TechnicianOperations,
    DealerStaffAdministration,
    FleetOperations,
    FactoryProvisioning,
    WaterFlexStaffAdministration
}

/// <summary>Lifecycle state of a staff identity as it is provisioned, used, and eventually deprovisioned.</summary>
public enum StaffIdentityState
{
    PendingActivation,
    Active,
    Suspended,
    Deprovisioning,
    Failed
}

/// <summary>Lifecycle state of an outstanding staff invitation, from creation through acceptance or expiry.</summary>
public enum StaffInvitationStatus
{
    PendingProvisioning,
    Ready,
    Accepted,
    Revoked,
    Expired,
    Failed
}

/// <summary>Processing state of a background staff-provisioning work item (e.g. Cloudflare Access grant creation).</summary>
public enum StaffProvisioningWorkStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}

/// <summary>Maps each <see cref="StaffRole"/> to the capabilities it grants, and flags roles that need dealer scoping or privileged access.</summary>
public static class StaffRoleCapabilities
{
    /// <summary>Returns true if the given role includes the given capability.</summary>
    public static bool HasCapability(this StaffRole role, StaffCapability capability) =>
        (role, capability) switch
        {
            (StaffRole.DealerAdministrator, StaffCapability.StaffAdministration) => true,
            (StaffRole.WaterFlexAdministrator, StaffCapability.StaffAdministration) => true,
            (StaffRole.DealerTechnician, StaffCapability.TechnicianOperations) => true,
            (StaffRole.DealerAdministrator, StaffCapability.TechnicianOperations) => true,
            (StaffRole.DealerAdministrator, StaffCapability.DealerStaffAdministration) => true,
            (StaffRole.DealerAdministrator, StaffCapability.FleetOperations) => true,
            (StaffRole.WaterFlexEmployee, StaffCapability.FleetOperations) => true,
            (StaffRole.WaterFlexAdministrator, StaffCapability.FleetOperations) => true,
            (StaffRole.FactoryWorker, StaffCapability.FactoryProvisioning) => true,
            (StaffRole.WaterFlexAdministrator, StaffCapability.FactoryProvisioning) => true,
            (StaffRole.WaterFlexAdministrator, StaffCapability.WaterFlexStaffAdministration) => true,
            (StaffRole.WaterFlexAdministrator, StaffCapability.TechnicianOperations) => true,
            _ => false
        };

    /// <summary>Returns true if the role is internal WaterFlex staff, which authenticates through the privileged Cloudflare Access tier rather than the dealer-facing one.</summary>
    public static bool RequiresPrivilegedAccessTier(this StaffRole role) =>
        role is StaffRole.FactoryWorker or StaffRole.WaterFlexEmployee or StaffRole.WaterFlexAdministrator;

    /// <summary>Returns true if the role must be scoped to a specific dealer.</summary>
    public static bool RequiresDealer(this StaffRole role) =>
        role is StaffRole.DealerTechnician or StaffRole.DealerAdministrator;
}

/// <summary>The authenticated staff identity performing an operation, carrying enough context to authorize and scope the request.</summary>
public sealed record StaffActor(
    string UserId,
    string DisplayName,
    StaffRole Role,
    string? DealerExternalId,
    string? DealerName);
