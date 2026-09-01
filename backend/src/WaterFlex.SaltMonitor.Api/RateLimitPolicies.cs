namespace WaterFlex.SaltMonitor.Api;

/// <summary>Named rate-limiter policies registered for the API, keyed by the kind of caller they apply to.</summary>
public static class RateLimitPolicies
{
    public const string Device = "device";
    public const string Activation = "activation";
    public const string Staff = "staff";
    public const string Factory = "factory";
}
