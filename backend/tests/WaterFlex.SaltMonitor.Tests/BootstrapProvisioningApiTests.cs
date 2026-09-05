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
    public async Task FactoryConfiguration_RequiresFactoryCapability()
    {
        await using var factory = new BootstrapApiFactory();
        using var workerClient = factory.CreateClient();
        workerClient.DefaultRequestHeaders.Add("X-WaterFlex-Development-User", "wf-factory-riley");
        using var employeeClient = factory.CreateClient();
        employeeClient.DefaultRequestHeaders.Add("X-WaterFlex-Development-User", "wf-ops-alex");

        var worker = await workerClient.GetAsync("/api/v1/factory/configuration");
        var employee = await employeeClient.GetAsync("/api/v1/factory/configuration");

        Assert.Equal(HttpStatusCode.OK, worker.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, employee.StatusCode);
    }

    [Fact]
    public async Task FactoryRegistrationThenTechnicianReservation_CreatesPendingSessionOnly()
    {
        await using var factory = new BootstrapApiFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var factoryRequest = new RegisterFactoryDeviceRequest(
            "factory-api-job-0001",
            "Arduino Nano ESP32",
            "wf_boot_api_0001",
            Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("api-bootstrap-secret"))),
            "wf-uart-pilot-0.2",
            "factory-v2");

        client.DefaultRequestHeaders.Add("X-WaterFlex-Request", "console");
        var unauthorized = await client.PostAsJsonAsync("/api/v1/factory/devices", factoryRequest);

        client.DefaultRequestHeaders.Add("X-WaterFlex-Development-User", "wf-factory-riley");
        var registered = await client.PostAsJsonAsync("/api/v1/factory/devices", factoryRequest);
        var registration = await registered.Content.ReadFromJsonAsync<FactoryDeviceRegistration>(JsonOptions);

        client.DefaultRequestHeaders.Remove("X-WaterFlex-Development-User");
        client.DefaultRequestHeaders.Remove("X-WaterFlex-Request");
        client.DefaultRequestHeaders.Add("X-WaterFlex-Development-User", "north-star-jordan");
        var sessionRequest = new CreateCommissioningSessionRequest(
            "WF-C-10482",
            "WF-L-10482-01",
            "WF-A-10482-S1",
            "WF-NANO-0001",
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
    public async Task ActiveFactoryDevice_ResumesCreatorsOwnJobAndHidesItFromOtherWorkers()
    {
        await using var factory = new BootstrapApiFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-WaterFlex-Request", "console");
        client.DefaultRequestHeaders.Add("X-WaterFlex-Development-User", "wf-factory-riley");
        var registered = await client.PostAsJsonAsync(
            "/api/v1/factory/devices",
            new RegisterFactoryDeviceRequest(
                "factory-active-job-0001",
                "Arduino Nano ESP32",
                "wf_boot_active_0001",
                Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("active-bootstrap-secret"))),
                "wf-uart-pilot-0.2",
                "factory-v2"));
        var registration = await registered.Content.ReadFromJsonAsync<FactoryDeviceRegistration>(JsonOptions);

        var resumedByCreator = await client.GetAsync("/api/v1/factory/devices/active");
        var resumedBody = await resumedByCreator.Content.ReadFromJsonAsync<FactoryDeviceRegistration>(JsonOptions);

        client.DefaultRequestHeaders.Remove("X-WaterFlex-Development-User");
        client.DefaultRequestHeaders.Add("X-WaterFlex-Development-User", "wf-admin-avery");
        var resumedByOther = await client.GetAsync("/api/v1/factory/devices/active");

        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        Assert.Equal(HttpStatusCode.OK, resumedByCreator.StatusCode);
        Assert.Equal(registration!.DeviceId, resumedBody!.DeviceId);
        Assert.Equal("factory-active-job-0001", resumedBody.IdempotencyKey);
        Assert.Equal(HttpStatusCode.NotFound, resumedByOther.StatusCode);
    }

    [Fact]
    public async Task FlashAuthorizationVerify_RequiresNoStaffSessionButOnlyAcceptsAValidTokenOnce()
    {
        await using var factory = new BootstrapApiFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-WaterFlex-Request", "console");
        client.DefaultRequestHeaders.Add("X-WaterFlex-Development-User", "wf-factory-riley");
        var registered = await client.PostAsJsonAsync(
            "/api/v1/factory/devices",
            new RegisterFactoryDeviceRequest(
                "factory-flash-job-0001",
                "Arduino Nano ESP32",
                "wf_boot_flash_0001",
                Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("flash-bootstrap-secret"))),
                "wf-uart-pilot-0.2",
                "factory-v2"));
        var registration = await registered.Content.ReadFromJsonAsync<FactoryDeviceRegistration>(JsonOptions);
        client.DefaultRequestHeaders.Remove("X-WaterFlex-Development-User");
        client.DefaultRequestHeaders.Remove("X-WaterFlex-Request");
        var noStationCredential = await client.PostAsJsonAsync(
            "/api/v1/factory/flash-authorizations/verify",
            new FlashAuthorizationVerificationRequest(registration!.DeviceId, registration.IdempotencyKey, "wf-uart-pilot-0.2", "factory-v2", new string('a', 64), registration.FlashAuthorizationToken!));
        var badToken = await factory.PostSignedAsync(client,
            new FlashAuthorizationVerificationRequest(registration!.DeviceId, registration.IdempotencyKey, "wf-uart-pilot-0.2", "factory-v2", new string('a', 64), "not-a-real-token"));
        var firstUse = await factory.PostSignedAsync(client,
            new FlashAuthorizationVerificationRequest(registration.DeviceId, registration.IdempotencyKey, "wf-uart-pilot-0.2", "factory-v2", new string('a', 64), registration.FlashAuthorizationToken!));
        var replay = await factory.PostSignedAsync(client,
            new FlashAuthorizationVerificationRequest(registration.DeviceId, registration.IdempotencyKey, "wf-uart-pilot-0.2", "factory-v2", new string('a', 64), registration.FlashAuthorizationToken!));

        Assert.NotNull(registration!.FlashAuthorizationToken);
        Assert.Equal(HttpStatusCode.Forbidden, noStationCredential.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, badToken.StatusCode);
        Assert.Equal(HttpStatusCode.OK, firstUse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, replay.StatusCode);
    }

    [Fact]
    public async Task DisabledFactoryProvisioning_LeavesConfigurationReadableAndRejectsRegistration()
    {
        await using var factory = new BootstrapApiFactory(factoryProvisioningEnabled: false);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-WaterFlex-Development-User", "wf-factory-riley");
        client.DefaultRequestHeaders.Add("X-WaterFlex-Request", "console");

        var configuration = await client.GetAsync("/api/v1/factory/configuration");
        var registration = await client.PostAsJsonAsync(
            "/api/v1/factory/devices",
            new RegisterFactoryDeviceRequest(
                "factory-disabled-job-0001",
                "Arduino Nano ESP32",
                "wf_boot_disabled_0001",
                Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("disabled-bootstrap-secret"))),
                "wf-uart-pilot-0.2",
                "factory-v2"));

        Assert.Equal(HttpStatusCode.OK, configuration.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, registration.StatusCode);
    }

    [Fact]
    public async Task AdministratorGrantCanBeRedeemedByUnauthenticatedHelperWithoutConsoleHeader()
    {
        await using var factory = new BootstrapApiFactory();
        await factory.InitializeDatabaseAsync();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(false); var raw = new byte[65]; raw[0] = 4;
        parameters.Q.X!.CopyTo(raw, 1); parameters.Q.Y!.CopyTo(raw, 33);
        var publicKey = Convert.ToBase64String(raw).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var thumbprint = Convert.ToHexStringLower(SHA256.HashData(raw));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-WaterFlex-Development-User", "wf-admin-avery");
        client.DefaultRequestHeaders.Add("X-WaterFlex-Request", "console");
        var grantResponse = await client.PostAsJsonAsync("/api/v1/factory/stations/enrollment-grants", new FactoryStationEnrollmentGrantRequest("Test PC", publicKey, thumbprint));
        var grant = await grantResponse.Content.ReadFromJsonAsync<WaterFlex.SaltMonitor.Provisioning.FactoryStationEnrollmentGrant>(JsonOptions);
        client.DefaultRequestHeaders.Clear();
        var enrollment = await client.PostAsJsonAsync("/api/v1/factory/stations/enroll", new EnrollFactoryStationRequest(grant!.GrantToken, "Test PC", publicKey, thumbprint, "software", "4.0.0", "4"));
        Assert.Equal(HttpStatusCode.OK, grantResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, enrollment.StatusCode);
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
        private readonly bool factoryProvisioningEnabled;
        private readonly ECDsa stationKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly Guid stationId = Guid.NewGuid();

        public BootstrapApiFactory(bool factoryProvisioningEnabled = true)
        {
            this.factoryProvisioningEnabled = factoryProvisioningEnabled;
            var databaseName = $"WaterFlexBootstrapApiTests_{Guid.NewGuid():N}";
            connectionString = TestPostgres.GetConnectionString(databaseName);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:SaltMonitor"] = connectionString,
                    ["FactoryProvisioning:Enabled"] = factoryProvisioningEnabled.ToString()
                }));
        }

        public async Task InitializeDatabaseAsync()
        {
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<SaltMonitorDbContext>();
            await context.Database.MigrateAsync();
            if (!await context.FactoryStations.AnyAsync(station => station.Id == stationId))
            {
                var parameters = stationKey.ExportParameters(false);
                var raw = new byte[65]; raw[0] = 4;
                parameters.Q.X!.CopyTo(raw, 1); parameters.Q.Y!.CopyTo(raw, 33);
                context.FactoryStations.Add(new FactoryStation
                {
                    Id = stationId, DisplayName = "Test Station", PublicKey = Base64Url(raw),
                    Thumbprint = Convert.ToHexStringLower(SHA256.HashData(raw)), KeyProviderType = "software",
                    HelperVersion = "4.0.0", ProtocolVersion = "4", EnrolledAtUtc = DateTimeOffset.UtcNow
                });
                await context.SaveChangesAsync();
            }
        }

        public async Task<HttpResponseMessage> PostSignedAsync<T>(HttpClient client, T value)
        {
            const string path = "/api/v1/factory/flash-authorizations/verify";
            var body = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var nonce = Base64Url(RandomNumberGenerator.GetBytes(16));
            var canonical = $"WF-STATION-V1\nPOST\n{path}\n{timestamp}\n{nonce}\n{Convert.ToHexStringLower(SHA256.HashData(body))}";
            var signature = stationKey.SignData(Encoding.UTF8.GetBytes(canonical), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(body) };
            request.Content.Headers.ContentType = new("application/json");
            request.Headers.Add("X-WaterFlex-Station-Id", stationId.ToString());
            request.Headers.Add("X-WaterFlex-Station-Timestamp", timestamp);
            request.Headers.Add("X-WaterFlex-Station-Nonce", nonce);
            request.Headers.Add("X-WaterFlex-Station-Signature", Base64Url(signature));
            return await client.SendAsync(request);
        }

        private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

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
