using WaterFlex.SaltMonitor.Domain.Model;

namespace WaterFlex.SaltMonitor.Domain.Level;

/// <summary>Converts a measured ultrasonic distance into a tank fill percentage.</summary>
public static class FillCalculator
{
    /// <summary>
    /// fillPct = clamp((tankDepth - measuredDistance) / tankDepth * 100, 0, 100).
    /// Tank depth is measured from the sensor face to the tank bottom, using the same origin as
    /// every sensor reading.
    /// </summary>
    public static double CalculateFillPercent(int measuredDistanceMm, TankCalibration calibration)
    {
        if (calibration.TankDepthMm <= 0)
        {
            throw new ArgumentException("Tank depth must be greater than zero.", nameof(calibration));
        }

        var pct = (double)(calibration.TankDepthMm - measuredDistanceMm)
            / calibration.TankDepthMm
            * 100.0;
        return Math.Clamp(pct, 0.0, 100.0);
    }
}
