using System.Globalization;

namespace WaterFlex.SaltMonitor.Provisioning;

/// <summary>
/// Formats and parses the canonical WF-NANO-#### serial number format that the server assigns to
/// factory-registered devices, replacing serials that used to be supplied by the factory itself.
/// </summary>
public static class FactorySerialNumber
{
    /// <summary>Renders a server-issued sequence number as the canonical WF-NANO-#### serial, zero-padded to at least 4 digits.</summary>
    public static string Format(long sequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        return $"WF-NANO-{sequence.ToString("D4", CultureInfo.InvariantCulture)}";
    }

    /// <summary>Attempts to recover the underlying sequence number from a canonical serial string.</summary>
    public static bool TryParse(string? serialNumber, out long sequence)
    {
        sequence = 0;
        const string prefix = "WF-NANO-";
        return serialNumber is not null
            && serialNumber.StartsWith(prefix, StringComparison.Ordinal)
            && serialNumber.Length >= prefix.Length + 4
            && long.TryParse(
                serialNumber.AsSpan(prefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out sequence)
            && sequence > 0;
    }
}
