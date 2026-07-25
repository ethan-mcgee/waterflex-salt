using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WaterFlex.SaltMonitor.Infrastructure.Persistence;
using WaterFlex.SaltMonitor.Ingestion;
using Xunit;

namespace WaterFlex.SaltMonitor.Tests;

public sealed class CommissioningApiTests
{
    [Fact]
    public async Task TechnicianWorkflow_SearchesDirectoryAndCommissionsSensor()
    {
        await using var factory = new CommissioningApiFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-WaterFlex-Development-User", "north-star-jordan");

        var customers = await client.GetFromJsonAsync<WaterFlexCustomerOption[]>(
            "/api/v1/technician/customers?search=North");
        var request = new CommissionSensorRequest(
            "WF-C-10482",
            "WF-L-10482-01",
            "WF-A-10482-S1",
            "WF-NANO-API-01",
            "A1B2C3D4E5F8",
            "Arduino Nano ESP32",
            "WO-API-1001",
            150m,
            50m);

        var response = await client.PostAsJsonAsync("/api/v1/technician/commission", request);
        var commissioning = await response.Content.ReadFromJsonAsync<CommissionSensorResponse>();

        var customer = Assert.Single(customers!);
        Assert.Equal("North Ridge Apartments", customer.DisplayName);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(commissioning);
        Assert.StartsWith("wf_", commissioning.DeviceToken);
        Assert.Equal(1, await factory.CountDevicesAsync());
        Assert.Equal(1, await factory.CountInstallationsAsync());
        Assert.Equal(1, await factory.CountDealersAsync());
    }

    private sealed class CommissioningApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;

        public CommissioningApiFactory()
        {
            var databaseName = $"WaterFlexCommissioningApiTests_{Guid.NewGuid():N}";
            _connectionString = $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True";
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:SaltMonitor"] = _connectionString
                }));
        }

            public async Task InitializeDatabaseAsync()
            {
                using var scope = Services.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<SaltMonitorDbContext>();
                await context.Database.MigrateAsync();
            }

        public async Task<int> CountDevicesAsync()
        {
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<SaltMonitorDbContext>();
            return await context.Devices.CountAsync();
        }

        public async Task<int> CountInstallationsAsync()
        {
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<SaltMonitorDbContext>();
            return await context.DeviceInstallations.CountAsync();
        }

        public async Task<int> CountDealersAsync()
        {
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<SaltMonitorDbContext>();
            return await context.Dealers.CountAsync();
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