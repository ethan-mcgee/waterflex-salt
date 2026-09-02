namespace WaterFlex.SaltMonitor.Api;

/// <summary>Environment-controlled factory workflow settings returned to the staff console.</summary>
public sealed class FactoryProvisioningOptions
{
    public const string SectionName = "FactoryProvisioning";

    public bool Enabled { get; set; }
    public string Model { get; set; } = "Arduino Nano ESP32";
    public string ApprovedFirmwareVersion { get; set; } = "wf-uart-pilot-0.1";
    public string ConfigurationVersion { get; set; } = "factory-v2";
    public string HelperBaseUrl { get; set; } = "http://127.0.0.1:8765";
    public string HelperProtocolVersion { get; set; } = "1";
    public string? BundleBucket { get; set; }
    public string? BundleKeyPrefix { get; set; }
}

/// <summary>Non-secret settings the factory console needs before starting a local provisioning job.</summary>
public sealed record FactoryProvisioningConfiguration(
    bool Enabled,
    string Model,
    string ApprovedFirmwareVersion,
    string ConfigurationVersion,
    string HelperBaseUrl,
    string HelperProtocolVersion);

/// <summary>A presigned download for the approved factory firmware bundle, returned to the local helper.</summary>
public sealed record FactoryBundleDownload(
    string Model,
    string FirmwareVersion,
    string ConfigurationVersion,
    string HelperProtocolVersion,
    string DownloadUrl,
    string Sha256,
    DateTimeOffset ExpiresAtUtc);
