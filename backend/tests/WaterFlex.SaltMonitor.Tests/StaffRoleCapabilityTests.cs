using WaterFlex.SaltMonitor.Domain.Security;
using Xunit;

namespace WaterFlex.SaltMonitor.Tests;

public sealed class StaffRoleCapabilityTests
{
    [Theory]
    [InlineData(StaffRole.DealerTechnician, StaffCapability.TechnicianOperations, true)]
    [InlineData(StaffRole.DealerAdministrator, StaffCapability.TechnicianOperations, true)]
    [InlineData(StaffRole.DealerAdministrator, StaffCapability.StaffAdministration, true)]
    [InlineData(StaffRole.DealerTechnician, StaffCapability.StaffAdministration, false)]
    [InlineData(StaffRole.WaterFlexEmployee, StaffCapability.FleetOperations, true)]
    [InlineData(StaffRole.WaterFlexAdministrator, StaffCapability.FleetOperations, true)]
    [InlineData(StaffRole.WaterFlexAdministrator, StaffCapability.StaffAdministration, true)]
    [InlineData(StaffRole.WaterFlexEmployee, StaffCapability.StaffAdministration, false)]
    [InlineData(StaffRole.DealerAdministrator, StaffCapability.FleetOperations, true)]
    [InlineData(StaffRole.WaterFlexAdministrator, StaffCapability.TechnicianOperations, true)]
    [InlineData(StaffRole.WaterFlexEmployee, StaffCapability.TechnicianOperations, false)]
    public void Matrix_IsExplicitAndFailClosed(StaffRole role, StaffCapability capability, bool expected) =>
        Assert.Equal(expected, role.HasCapability(capability));
}
