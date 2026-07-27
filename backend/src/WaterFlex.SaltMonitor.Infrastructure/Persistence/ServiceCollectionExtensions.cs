using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using WaterFlex.SaltMonitor.Domain.Abstractions;
using WaterFlex.SaltMonitor.Infrastructure;
using WaterFlex.SaltMonitor.Ingestion;
using WaterFlex.SaltMonitor.Operations;
using WaterFlex.SaltMonitor.Provisioning;

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSaltMonitorPersistence(this IServiceCollection services)
    {
        services.AddDbContext<SaltMonitorDbContext>((serviceProvider, options) =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var environment = serviceProvider.GetRequiredService<IHostEnvironment>();
            var connectionString = configuration.GetConnectionString("SaltMonitor");
            if (connectionString is null && environment.IsDevelopment())
            {
                connectionString = "Server=(localdb)\\MSSQLLocalDB;Database=WaterFlexSaltMonitor;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";
            }

            if (connectionString is null)
            {
                throw new InvalidOperationException(
                    "Connection string 'SaltMonitor' is required. Set ConnectionStrings__SaltMonitor.");
            }

            options.UseSqlServer(connectionString, sqlServer => sqlServer.EnableRetryOnFailure());
        });
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<TelemetryBatchValidator>();
        services.TryAddSingleton<IDeliveryTicketGateway, StubDeliveryTicketGateway>();
        services.AddSingleton<IDevelopmentIdentityDirectory, DevelopmentIdentityDirectory>();
        services.AddSingleton<IWaterFlexCustomerDirectory, DevelopmentWaterFlexCustomerDirectory>();
        services.AddScoped<IDeviceTokenValidator, DeviceTokenValidator>();
        services.AddScoped<ITelemetryIngestionService, EfTelemetryIngestionService>();
        services.AddScoped<ISensorCommissioningService, EfSensorCommissioningService>();
        services.AddScoped<IFleetQueryService, EfFleetQueryService>();
        services.AddScoped<IFactoryDeviceRegistrationService, EfFactoryDeviceRegistrationService>();
        services.AddScoped<ICommissioningSessionService, EfCommissioningSessionService>();
        services.AddScoped<IDeviceBootstrapActivationService, EfDeviceBootstrapActivationService>();

        return services;
    }
}