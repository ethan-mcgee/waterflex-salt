using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WaterFlex.SaltMonitor.Infrastructure.Persistence;
using WaterFlex.SaltMonitor.Provisioning;
using Xunit;

namespace WaterFlex.SaltMonitor.Tests;

public sealed class BootstrapProvisioningApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact]
    public async Task FactoryRegistrationThenTechnicianReservation_CreatesPendingSessionOnly()
    {
        await using var factory = new BootstrapApiFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var factoryRequest = new RegisterFactoryDeviceRequest(
            "WF-BOOT-API-01",
            "A1:B2:C3:D4:E5:F9",
            "Arduino Nano ESP32",
            "wf_boot_api_0001",
            Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("api-bootstrap-secret"))),
            "1.0.0",
            "pilot-v1");

        var unauthorized = await client.PostAsJsonAsync("/api/v1/factory/devices", factoryRequest);

        client.DefaultRequestHeaders.Add("X-WaterFlex-Factory-Key", "factory-test-key");
        client.DefaultRequestHeaders.Add("X-WaterFlex-Factory-Operator", "factory-api-test");
        var registered = await client.PostAsJsonAsync("/api/v1/factory/devices", factoryRequest);
        var registration = await registered.Content.ReadFromJsonAsync<FactoryDeviceRegistration>();

        client.DefaultRequestHeaders.Remove("X-WaterFlex-Factory-Key");
        client.DefaultRequestHeaders.Remove("X-WaterFlex-Factory-Operator");
        client.DefaultRequestHeaders.Add("X-WaterFlex-Development-User", "north-star-jordan");
        var sessionRequest = new CreateCommissioningSessionRequest(
            "WF-C-10482",
            "WF-L-10482-01",
            "WF-A-10482-S1",
            "WF-BOOT-API-01",
            "WO-BOOT-API-01",
            150m);
        var reserved = await client.PostAsJsonAsync(
            "/api/v1/technician/commissioning-sessions",
            sessionRequest);
        var session = await reserved.Content.ReadFromJsonAsync<CommissioningSessionView>(JsonOptions);
        var status = await client.GetAsync(
            $"/api/v1/technician/commissioning-sessions/{session!.SessionId:D}");

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        Assert.NotNull(registration);
        Assert.Equal(HttpStatusCode.Created, reserved.StatusCode);
        Assert.Equal(CommissioningSessionStatus.PendingSensor, session.Status);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal(1, await factory.CountAsync(database => database.Devices));
        Assert.Equal(1, await factory.CountAsync(database => database.DeviceBootstrapCredentials));
        Assert.Equal(1, await factory.CountAsync(database => database.CommissioningSessions));
        Assert.Equal(0, await factory.CountAsync(database => database.DeviceInstallations));
        Assert.Equal(0, await factory.CountAsync(database => database.DeviceCredentials));
    }

    [Fact]
    public async Task TechnicianCanVerifyWorkOrderWithoutSeeingWaterFlexIds()
    {
        await using var factory = new BootstrapApiFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-WaterFlex-Development-User", "north-star-jordan");

        var response = await client.GetAsync("/api/v1/technician/installation-work-orders/WO-82418");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Baker Family Residence", body, StringComparison.Ordinal);
        Assert.DoesNotContain("waterFlexCustomerId", body, StringComparison.Ordinal);
        Assert.DoesNotContain("waterFlexAssetId", body, StringComparison.Ordinal);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private sealed class BootstrapApiFactory : WebApplicationFactory<Program>
    {
        private readonly string connectionString;

        public BootstrapApiFactory()
        {
            var databaseName = $"WaterFlexBootstrapApiTests_{Guid.NewGuid():N}";
            connectionString = $"Host=localhost;Port=5432;Database={databaseName};Username=postgres;Password=postgres";
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:SaltMonitor"] = connectionString,
                    ["FactoryProvisioning:DevelopmentKey"] = "factory-test-key"
                }));
        }

        public async Task InitializeDatabaseAsync()
        {
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<SaltMonitorDbContext>();
            await context.Database.MigrateAsync();
        }

        public async Task<int> CountAsync<TEntity>(
            Func<SaltMonitorDbContext, DbSet<TEntity>> setSelector)
            where TEntity : class
        {
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<SaltMonitorDbContext>();
            return await setSelector(context).CountAsync();
        }

        public override async ValueTask DisposeAsync()
        {
            using (var scope = Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<SaltMonitorDbContext>();
                await context.Database.EnsureDeletedAsync();
            }
            await base.DisposeAsync();
        }
    }
}