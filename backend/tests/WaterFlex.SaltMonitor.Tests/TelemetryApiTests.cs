using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WaterFlex.SaltMonitor.Infrastructure.Persistence;
using WaterFlex.SaltMonitor.Ingestion;
using Xunit;

namespace WaterFlex.SaltMonitor.Tests;

public sealed class TelemetryApiTests
{
    [Fact]
    public async Task TelemetryEndpoint_AuthenticatesPersistsAndAcknowledgesRetry()
    {
        await using var factory = new TelemetryApiFactory();
        var token = await factory.SeedAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var reading = new TelemetryReadingInput(
            Guid.NewGuid(), 1, DateTimeOffset.UtcNow, 1000, 1000, 90, 8, -60);
        var batch = new TelemetryBatch(1, "1.0.0", [reading]);

        var acceptedResponse = await client.PostAsJsonAsync("/api/v1/device/telemetry", batch);
        var duplicateResponse = await client.PostAsJsonAsync("/api/v1/device/telemetry", batch);

        Assert.Equal(HttpStatusCode.OK, acceptedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, duplicateResponse.StatusCode);
        Assert.Contains("\"status\":\"accepted\"", await acceptedResponse.Content.ReadAsStringAsync());
        Assert.Contains("\"status\":\"duplicate\"", await duplicateResponse.Content.ReadAsStringAsync());
        Assert.Equal(1, await factory.CountReadingsAsync());
    }

    [Fact]
    public async Task TelemetryEndpoint_RejectsUnknownToken()
    {
        await using var factory = new TelemetryApiFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var secret = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        client.DefaultRequestHeaders.Authorization = new("Bearer", $"unknown.{secret}");
        var reading = new TelemetryReadingInput(
            Guid.NewGuid(), 1, DateTimeOffset.UtcNow, 1000, 1000, 90, 8, -60);

        var response = await client.PostAsJsonAsync(
            "/api/v1/device/telemetry",
            new TelemetryBatch(1, "1.0.0", [reading]));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TelemetryEndpoint_RejectsCustomerOwnershipFields()
    {
        await using var factory = new TelemetryApiFactory();
        var token = await factory.SeedAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var bootId = Guid.NewGuid();
        var body = $$"""
            {
              "schemaVersion": 1,
              "firmwareVersion": "1.0.0",
              "customerId": "forged-customer",
              "readings": [
                {
                  "bootId": "{{bootId}}",
                  "sequenceNumber": 1,
                  "uptimeMilliseconds": 1000,
                  "rawDistanceMm": 1000,
                  "quality": 90,
                  "sampleCount": 8,
                  "wifiRssiDbm": -60
                }
              ]
            }
            """;

        var response = await client.PostAsync(
            "/api/v1/device/telemetry",
            new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await factory.CountReadingsAsync());
    }

    [Fact]
    public async Task DeviceHealthEndpoint_StoresFaultWithoutCreatingReading()
    {
        await using var factory = new TelemetryApiFactory();
        var token = await factory.SeedAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var heartbeat = new DeviceHealthHeartbeat(
            1,
            "uart-pilot-0.1",
            DateTimeOffset.UtcNow,
            12_000,
            SensorHealthStatus.Faulted,
            SensorFaultCode.ReadTimeout,
            -58,
            0,
            true,
            3);

        var response = await client.PostAsJsonAsync("/api/v1/device/health", heartbeat);
        var device = await factory.GetDeviceAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, await factory.CountReadingsAsync());
        Assert.Equal(SensorHealthStatus.Faulted, device.LastSensorStatus);
        Assert.Equal(SensorFaultCode.ReadTimeout, device.LastSensorFault);
        Assert.Equal("uart-pilot-0.1", device.LastHealthFirmwareVersion);
        Assert.Equal(3, device.LastDroppedReadingCount);
        Assert.NotNull(device.LastHealthReportedAtUtc);
    }

    [Fact]
    public async Task DeviceHealthEndpoint_RejectsUnknownSensorState()
    {
        await using var factory = new TelemetryApiFactory();
        var token = await factory.SeedAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var body = """
            {
              "schemaVersion": 1,
              "firmwareVersion": "uart-pilot-0.1",
              "uptimeMilliseconds": 12000,
              "sensorStatus": 99,
              "sensorFault": null,
              "wifiRssiDbm": -58,
              "queuedReadingCount": 0,
              "clockSynchronized": true
            }
            """;

        var response = await client.PostAsync(
            "/api/v1/device/health",
            new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await factory.CountReadingsAsync());
    }

    [Fact]
    public async Task OpenApiDocument_DescribesDeviceBearerAuthentication()
    {
        await using var factory = new TelemetryApiFactory();
        using var client = factory.CreateClient();

        using var document = JsonDocument.Parse(
            await client.GetStringAsync("/openapi/v1.json"));
        var root = document.RootElement;
        var securityScheme = root
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("DeviceToken");
        var telemetryOperation = root
            .GetProperty("paths")
            .GetProperty("/api/v1/device/telemetry")
            .GetProperty("post");
        var deviceHealthOperation = root
            .GetProperty("paths")
            .GetProperty("/api/v1/device/health")
            .GetProperty("post");
        var healthOperation = root
            .GetProperty("paths")
            .GetProperty("/health")
            .GetProperty("get");

        Assert.StartsWith("3.1", root.GetProperty("openapi").GetString());
        Assert.Equal("http", securityScheme.GetProperty("type").GetString());
        Assert.Equal("bearer", securityScheme.GetProperty("scheme").GetString());
        Assert.Contains(
            telemetryOperation.GetProperty("security").EnumerateArray(),
            requirement => requirement.TryGetProperty("DeviceToken", out _));
        Assert.Contains(
            deviceHealthOperation.GetProperty("security").EnumerateArray(),
            requirement => requirement.TryGetProperty("DeviceToken", out _));
        Assert.False(healthOperation.TryGetProperty("security", out _));
    }

    [Fact]
    public async Task SwaggerUi_LoadsInteractiveDocumentation()
    {
        await using var factory = new TelemetryApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/index.html");
        var html = await response.Content.ReadAsStringAsync();
        var initializer = await client.GetStringAsync("/swagger/index.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("swagger-ui", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/openapi/v1.json", initializer);
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class TelemetryApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;

        public TelemetryApiFactory()
        {
            var databaseName = $"WaterFlexSaltMonitorApiTests_{Guid.NewGuid():N}";
            _connectionString = TestPostgres.GetConnectionString(databaseName);
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

        public async Task<string> SeedAsync()
        {
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<SaltMonitorDbContext>();
            await context.Database.MigrateAsync();
            var now = DateTimeOffset.UtcNow;
            var customer = new CustomerAccount
            {
                Id = Guid.NewGuid(),
                WaterFlexCustomerId = $"customer-{Guid.NewGuid():N}",
                DisplayName = "API Test Customer",
                IsActive = true,
                LastSyncedAtUtc = now
            };
            var location = new ServiceLocation
            {
                Id = Guid.NewGuid(),
                CustomerAccountId = customer.Id,
                WaterFlexLocationId = $"location-{Guid.NewGuid():N}",
                DisplayName = "API Test Location",
                IsActive = true,
                LastSyncedAtUtc = now
            };
            var tank = new Tank
            {
                Id = Guid.NewGuid(),
                ServiceLocationId = location.Id,
                Label = "Softener 1",
                IsActive = true
            };
            var device = new Device
            {
                Id = Guid.NewGuid(),
                SerialNumber = $"WF-{Guid.NewGuid():N}",
                HardwareId = Guid.NewGuid().ToString("N")[..12],
                Model = "Nano ESP32",
                Status = DeviceLifecycleStatus.Active,
                RegisteredAtUtc = now,
                CommissionedAtUtc = now
            };
            var installation = new DeviceInstallation
            {
                Id = Guid.NewGuid(),
                DeviceId = device.Id,
                TankId = tank.Id,
                InstalledAtUtc = now
            };
            var calibration = new TankCalibrationRecord
            {
                Id = Guid.NewGuid(),
                DeviceInstallationId = installation.Id,
                Version = 1,
                TankDepthMm = 1500,
                CommissioningDistanceMm = 500,
                EffectiveFromUtc = now,
                CreatedBy = "api-test",
                CreatedAtUtc = now
            };
            var secret = RandomNumberGenerator.GetBytes(32);
            const string credentialId = "api-test-credential";
            var credential = new DeviceCredential
            {
                Id = Guid.NewGuid(),
                DeviceId = device.Id,
                CredentialId = credentialId,
                SecretHash = SHA256.HashData(secret),
                ValidFromUtc = now.AddMinutes(-1)
            };

            context.AddRange(customer, location, tank, device, installation, calibration, credential);
            await context.SaveChangesAsync();
            return $"{credentialId}.{Base64UrlEncode(secret)}";
        }

        public async Task InitializeDatabaseAsync()
        {
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<SaltMonitorDbContext>();
            await context.Database.MigrateAsync();
        }

        public async Task<int> CountReadingsAsync()
        {
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<SaltMonitorDbContext>();
            return await context.TelemetryReadings.CountAsync();
        }

        public async Task<Device> GetDeviceAsync()
        {
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<SaltMonitorDbContext>();
            return await context.Devices.AsNoTracking().SingleAsync();
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
