using Microsoft.Extensions.DependencyInjection;
using WaterFlex.SaltMonitor.Domain.Abstractions;
using WaterFlex.SaltMonitor.Infrastructure;
using WaterFlex.SaltMonitor.Infrastructure.Persistence;
using Xunit;

namespace WaterFlex.SaltMonitor.Tests;

public sealed class ServiceRegistrationTests
{
    [Fact]
    public void AddSaltMonitorPersistence_RegistersDeliveryTicketStub()
    {
        var services = new ServiceCollection();
        services.AddSaltMonitorPersistence();

        using var provider = services.BuildServiceProvider();
        var gateway = provider.GetRequiredService<IDeliveryTicketGateway>();

        Assert.IsType<StubDeliveryTicketGateway>(gateway);
    }
}