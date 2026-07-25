using WaterFlex.SaltMonitor.Domain.Monitoring;
using Xunit;

namespace WaterFlex.SaltMonitor.Tests;

public sealed class MonitoringPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, DeviceReportingStatus.Reporting)]
    [InlineData(2, DeviceReportingStatus.Reporting)]
    [InlineData(2.01, DeviceReportingStatus.Stale)]
    [InlineData(6, DeviceReportingStatus.Stale)]
    [InlineData(6.01, DeviceReportingStatus.Offline)]
    public void GetReportingStatus_UsesExpectedBoundaries(
        double ageHours,
        DeviceReportingStatus expected)
    {
        var status = MonitoringPolicy.GetReportingStatus(Now.AddHours(-ageHours), Now);

        Assert.Equal(expected, status);
    }

    [Fact]
    public void GetReportingStatus_ReturnsNeverReportedWithoutReading() =>
        Assert.Equal(
            DeviceReportingStatus.NeverReported,
            MonitoringPolicy.GetReportingStatus(null, Now));

    [Theory]
    [InlineData(34.99, true)]
    [InlineData(35, false)]
    public void IsBelowFillThreshold_UsesStrictThreshold(double fillPercent, bool expected) =>
        Assert.Equal(expected, MonitoringPolicy.IsBelowFillThreshold(fillPercent));
}