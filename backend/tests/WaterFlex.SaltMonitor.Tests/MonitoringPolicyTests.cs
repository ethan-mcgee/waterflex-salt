using WaterFlex.SaltMonitor.Domain.Monitoring;
using Xunit;

namespace WaterFlex.SaltMonitor.Tests;

public sealed class MonitoringPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly MonitoringSchedule Schedule = new(TimeSpan.FromMinutes(1));

    [Theory]
    [InlineData(0, DeviceReportingStatus.Reporting)]
    [InlineData(2.99, DeviceReportingStatus.Reporting)]
    [InlineData(3, DeviceReportingStatus.Stale)]
    [InlineData(4.99, DeviceReportingStatus.Stale)]
    [InlineData(5, DeviceReportingStatus.Offline)]
    public void GetReportingStatus_UsesExpectedBoundaries(
        double ageMinutes,
        DeviceReportingStatus expected)
    {
        var status = MonitoringPolicy.GetReportingStatus(
            Now.AddMinutes(-ageMinutes),
            Now,
            Schedule);

        Assert.Equal(expected, status);
    }

    [Fact]
    public void GetReportingStatus_ReturnsNeverReportedWithoutReading() =>
        Assert.Equal(
            DeviceReportingStatus.NeverReported,
            MonitoringPolicy.GetReportingStatus(null, Now, Schedule));

    [Theory]
    [InlineData(34.99, true)]
    [InlineData(35, false)]
    public void IsBelowFillThreshold_UsesStrictThreshold(double fillPercent, bool expected) =>
        Assert.Equal(expected, MonitoringPolicy.IsBelowFillThreshold(fillPercent));
}