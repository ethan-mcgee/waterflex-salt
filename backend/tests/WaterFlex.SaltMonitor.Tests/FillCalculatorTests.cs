using WaterFlex.SaltMonitor.Domain.Level;
using WaterFlex.SaltMonitor.Domain.Model;
using Xunit;

namespace WaterFlex.SaltMonitor.Tests;

public class FillCalculatorTests
{
    private static readonly TankCalibration Calibration = new(TankDepthMm: 1000);

    [Fact]
    public void Surface_at_sensor_is_100_percent()
    {
        Assert.Equal(100.0, FillCalculator.CalculateFillPercent(0, Calibration), 3);
    }

    [Fact]
    public void Surface_at_tank_bottom_is_0_percent()
    {
        Assert.Equal(0.0, FillCalculator.CalculateFillPercent(1000, Calibration), 3);
    }

    [Fact]
    public void Midpoint_is_50_percent()
    {
        Assert.Equal(50.0, FillCalculator.CalculateFillPercent(500, Calibration), 3);
    }

    [Fact]
    public void Reading_beyond_tank_bottom_clamps_to_0()
    {
        Assert.Equal(0.0, FillCalculator.CalculateFillPercent(1200, Calibration), 3);
    }

    [Fact]
    public void Invalid_calibration_throws()
    {
        var bad = new TankCalibration(TankDepthMm: 0);
        Assert.Throws<ArgumentException>(() => FillCalculator.CalculateFillPercent(400, bad));
    }
}
