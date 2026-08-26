using Microsoft.Extensions.DependencyInjection;
using WaterFlex.SaltMonitor.Domain.Abstractions;
using WaterFlex.SaltMonitor.Infrastructure;
using WaterFlex.SaltMonitor.Infrastructure.Persistence;
using WaterFlex.SaltMonitor.Operations;
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

    [Fact]
    public void AddSaltMonitorPersistence_RegistersDeliveryTicketWorkProcessor()
    {
        var services = new ServiceCollection();
        services.AddSaltMonitorPersistence();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<IDeliveryTicketWorkProcessor>();

        Assert.IsType<EfDeliveryTicketWorkProcessor>(processor);
    }
}