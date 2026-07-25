using WaterFlex.SaltMonitor.Domain.Monitoring;

namespace WaterFlex.SaltMonitor.Rules;

/// <summary>
/// Evaluates whether a tank's fill level warrants a delivery. Debounce, dedupe,
/// open-ticket suppression, and post-delivery cooldown are layered on top (see Plan C).
/// </summary>
public sealed class LowSaltEvaluator
{
    public const double ThresholdPercent = MonitoringPolicy.LowFillThresholdPercent;

    /// <summary>Returns true when the fill level is below the delivery threshold.</summary>
    public bool ShouldTrigger(double fillPercent) => MonitoringPolicy.IsBelowFillThreshold(fillPercent);
}
