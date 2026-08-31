using WaterFlex.SaltMonitor.Provisioning;
using Xunit;

namespace WaterFlex.SaltMonitor.Tests;

public sealed class FactorySerialNumberTests
{
    [Theory]
    [InlineData(1, "WF-NANO-0001")]
    [InlineData(9998, "WF-NANO-9998")]
    [InlineData(9999, "WF-NANO-9999")]
    [InlineData(10000, "WF-NANO-10000")]
    [InlineData(10001, "WF-NANO-10001")]
    public void Format_UsesFourDigitsAsMinimumWidth(long sequence, string expected) =>
        Assert.Equal(expected, FactorySerialNumber.Format(sequence));

    [Theory]
    [InlineData("WF-NANO-0001", 1)]
    [InlineData("WF-NANO-10000", 10000)]
    public void TryParse_AcceptsCanonicalSerials(string serial, long expected)
    {
        Assert.True(FactorySerialNumber.TryParse(serial, out var sequence));
        Assert.Equal(expected, sequence);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("WF-NANO-001")]
    [InlineData("WF-NANO-ABCD")]
    [InlineData("WaterFlex-0001")]
    public void TryParse_RejectsNonCanonicalSerials(string? serial) =>
        Assert.False(FactorySerialNumber.TryParse(serial, out _));
}
