namespace WaterFlex.SaltMonitor.Domain.Security;

public enum StaffRole
{
    DealerTechnician,
    DealerAdministrator,
    WaterFlexEmployee,
    WaterFlexAdministrator
}

public enum StaffCapability
{
    StaffAdministration,
    TechnicianOperations,
    DealerStaffAdministration,
    FleetOperations,
    WaterFlexStaffAdministration
}

public enum StaffIdentityState
{
    PendingActivation,
    Active,
    Suspended,
    Deprovisioning,
    Failed
}

public enum StaffInvitationStatus
{
    PendingProvisioning,
    Ready,
    Accepted,
    Revoked,
    Expired,
    Failed
}

public enum StaffProvisioningWorkStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}

public static class StaffRoleCapabilities
{
    public static bool HasCapability(this StaffRole role, StaffCapability capability) =>
        (role, capability) switch
        {
            (StaffRole.DealerAdministrator, StaffCapability.StaffAdministration) => true,
            (StaffRole.WaterFlexAdministrator, StaffCapability.StaffAdministration) => true,
            (StaffRole.DealerTechnician, StaffCapability.TechnicianOperations) => true,
            (StaffRole.DealerAdministrator, StaffCapability.TechnicianOperations) => true,
            (StaffRole.DealerAdministrator, StaffCapability.DealerStaffAdministration) => true,
            (StaffRole.WaterFlexEmployee, StaffCapability.FleetOperations) => true,
            (StaffRole.WaterFlexAdministrator, StaffCapability.FleetOperations) => true,
            (StaffRole.WaterFlexAdministrator, StaffCapability.WaterFlexStaffAdministration) => true,
            (StaffRole.WaterFlexAdministrator, StaffCapability.TechnicianOperations) => true,
            _ => false
        };

    public static bool RequiresPrivilegedAccessTier(this StaffRole role) =>
        role is StaffRole.WaterFlexEmployee or StaffRole.WaterFlexAdministrator;

    public static bool RequiresDealer(this StaffRole role) =>
        role is StaffRole.DealerTechnician or StaffRole.DealerAdministrator;
}

public sealed record StaffActor(
    string UserId,
    string DisplayName,
    StaffRole Role,
    string? DealerExternalId,
    string? DealerName);
