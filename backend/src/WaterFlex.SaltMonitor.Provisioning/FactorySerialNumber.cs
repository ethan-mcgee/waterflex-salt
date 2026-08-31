using System.Globalization;

namespace WaterFlex.SaltMonitor.Provisioning;

public static class FactorySerialNumber
{
    public static string Format(long sequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        return $"WF-NANO-{sequence.ToString("D4", CultureInfo.InvariantCulture)}";
    }

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
