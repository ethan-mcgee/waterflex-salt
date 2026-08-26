using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        // Resolving a scoped service that depends on SaltMonitorDbContext triggers the
        // AddDbContext options callback, which needs IConfiguration/IHostEnvironment even
        // though no connection is actually opened here.
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IHostEnvironment>(new StubHostEnvironment());
        services.AddSaltMonitorPersistence();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<IDeliveryTicketWorkProcessor>();

        Assert.IsType<EfDeliveryTicketWorkProcessor>(processor);
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = nameof(ServiceRegistrationTests);
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}