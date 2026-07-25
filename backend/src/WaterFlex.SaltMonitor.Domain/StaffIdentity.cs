namespace WaterFlex.SaltMonitor.Domain.Security;

public enum StaffRole
{
    DealerTechnician,
    WaterFlexEmployee
}

public sealed record StaffActor(
    string UserId,
    string DisplayName,
    StaffRole Role,
    string? DealerExternalId,
    string? DealerName);