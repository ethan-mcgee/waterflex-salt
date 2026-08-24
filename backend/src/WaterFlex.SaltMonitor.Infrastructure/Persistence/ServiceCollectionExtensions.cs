using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using WaterFlex.SaltMonitor.Domain.Abstractions;
using WaterFlex.SaltMonitor.Domain.Monitoring;
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
                connectionString = "Host=localhost;Port=5432;Database=WaterFlexSaltMonitor;Username=postgres;Password=postgres";
            }

            if (connectionString is null)
            {
                throw new InvalidOperationException(
                    "Connection string 'SaltMonitor' is required. Set ConnectionStrings__SaltMonitor.");
            }

            options.UseNpgsql(connectionString);
        });
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(serviceProvider =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var configuredValue = configuration["Monitoring:TelemetryIntervalSeconds"];
            var intervalSeconds = string.IsNullOrWhiteSpace(configuredValue)
                ? MonitoringSchedule.DefaultReportIntervalSeconds
                : int.TryParse(configuredValue, out var parsedValue)
                    && parsedValue is >= 1 and <= 86400
                        ? parsedValue
                        : throw new InvalidOperationException(
                            "Monitoring:TelemetryIntervalSeconds must be between 1 and 86400.");

            return new MonitoringSchedule(TimeSpan.FromSeconds(intervalSeconds));
        });
        services.TryAddSingleton<TelemetryBatchValidator>();
        services.TryAddSingleton<IDeliveryTicketGateway, StubDeliveryTicketGateway>();
        services.AddSingleton<IDevelopmentIdentityDirectory, DevelopmentIdentityDirectory>();
        services.AddSingleton<IWaterFlexCustomerDirectory, DevelopmentWaterFlexCustomerDirectory>();
        services.AddSingleton<IInstallationWorkOrderDirectory, DevelopmentInstallationWorkOrderDirectory>();
        services.AddScoped<IDeviceTokenValidator, DeviceTokenValidator>();
        services.AddScoped<IDeviceCredentialUsageRecorder, DeviceCredentialUsageRecorder>();
        services.AddScoped<ITelemetryIngestionService, EfTelemetryIngestionService>();
        services.AddScoped<IDeviceHealthService, EfDeviceHealthService>();
        services.AddScoped<ISensorCommissioningService, EfSensorCommissioningService>();
        services.AddScoped<IFleetQueryService, EfFleetQueryService>();
        services.AddScoped<IAlertOperationsService, EfAlertOperationsService>();
        services.AddScoped<IAlertWorkProcessor, EfAlertWorkProcessor>();
        services.AddScoped<IStaffAccessService, EfStaffAccessService>();
        services.AddScoped<IFactoryDeviceRegistrationService, EfFactoryDeviceRegistrationService>();
        services.AddScoped<ICommissioningSessionService, EfCommissioningSessionService>();
        services.AddScoped<IDeviceBootstrapActivationService, EfDeviceBootstrapActivationService>();
        services.AddSingleton(serviceProvider =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            return Microsoft.Extensions.Options.Options.Create(new TelemetryHistoryOptions
            {
                RawRetentionDays = ReadInt(configuration, "RawRetentionDays", 30),
                HourlyRetentionMonths = ReadInt(configuration, "HourlyRetentionMonths", 13),
                DailyRetentionYears = ReadInt(configuration, "DailyRetentionYears", 3),
                DeleteBatchSize = ReadInt(configuration, "DeleteBatchSize", 10_000),
                MaintenanceIntervalMinutes = ReadInt(configuration, "MaintenanceIntervalMinutes", 15)
            });
        });
        services.AddScoped<ITelemetryHistoryMaintenanceService, TelemetryHistoryMaintenanceService>();

        return services;
    }

    private static int ReadInt(IConfiguration configuration, string name, int defaultValue)
    {
        var value = configuration[$"{TelemetryHistoryOptions.SectionName}:{name}"];
        return string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : int.TryParse(value, out var parsed)
                ? parsed
                : throw new InvalidOperationException(
                    $"{TelemetryHistoryOptions.SectionName}:{name} must be an integer.");
    }
}
