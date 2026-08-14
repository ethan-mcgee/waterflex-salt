using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WaterFlex.SaltMonitor.Api;
using WaterFlex.SaltMonitor.Domain.Security;
using WaterFlex.SaltMonitor.Infrastructure.Persistence;
using Xunit;

namespace WaterFlex.SaltMonitor.Tests;

public sealed class StaffAuthenticationTests
{
    [Fact]
    public async Task Staging_RejectsDevelopmentHeaderAndDoesNotExposeDevelopmentDirectory()
    {
        await using var factory = new StaffApiFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add(DevelopmentIdentity.HeaderName, "wf-ops-alex");

        var opsResponse = await client.GetAsync("/api/v1/ops/dealers");
        var directoryResponse = await client.GetAsync("/api/v1/development/users");

        Assert.Equal(HttpStatusCode.Unauthorized, opsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, directoryResponse.StatusCode);
    }

    [Fact]
    public async Task Staging_MapsImmutableCloudflareSubjectToStoredWaterFlexRole()
    {
        await using var factory = new StaffApiFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add(StaffAuthenticationHandler.AccessAssertionHeader, "waterflex-employee");

        var response = await client.GetAsync("/api/v1/ops/dealers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Staging_RejectsDealerTechnicianFromWaterFlexOperations()
    {
        await using var factory = new StaffApiFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add(StaffAuthenticationHandler.AccessAssertionHeader, "dealer-technician");

        var response = await client.GetAsync("/api/v1/ops/dealers");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_VerifiesDatabaseSchemaAndStaffIdentityConfiguration()
    {
        await using var factory = new StaffApiFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed class StaffApiFactory : WebApplicationFactory<Program>, IAsyncDisposable
    {
        private const string Issuer = "https://waterflex.cloudflareaccess.com";
        private readonly string connectionString = TestPostgres.GetConnectionString(
            $"WaterFlexStaffTests_{Guid.NewGuid():N}");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Staging");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:SaltMonitor"] = connectionString,
                    ["CloudflareAccess:Issuer"] = Issuer,
                    ["CloudflareAccess:Audience"] = "pilot-audience"
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICloudflareAccessTokenValidator>();
                services.AddSingleton<ICloudflareAccessTokenValidator>(new FakeAccessTokenValidator(Issuer));
            });
        }

        public HttpClient CreateHttpsClient() => CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        public async Task InitializeAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<SaltMonitorDbContext>();
            await database.Database.MigrateAsync();
            var dealer = new Dealer
            {
                Id = Guid.NewGuid(),
                ExternalId = "WF-D-TEST",
                DisplayName = "Test Dealer",
                IsActive = true
            };
            database.AddRange(
                dealer,
                new StaffIdentityRecord
                {
                    Id = Guid.NewGuid(),
                    Issuer = Issuer,
                    Subject = "employee-subject",
                    Email = "operator@example.test",
                    DisplayName = "Pilot Operator",
                    Role = StaffRole.WaterFlexEmployee,
                    IsActive = true,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                },
                new StaffIdentityRecord
                {
                    Id = Guid.NewGuid(),
                    Issuer = Issuer,
                    Subject = "dealer-subject",
                    Email = "technician@example.test",
                    DisplayName = "Pilot Technician",
                    Role = StaffRole.DealerTechnician,
                    DealerId = dealer.Id,
                    IsActive = true,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                });
            await database.SaveChangesAsync();
        }

        async ValueTask IAsyncDisposable.DisposeAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<SaltMonitorDbContext>();
            await database.Database.EnsureDeletedAsync();
            await base.DisposeAsync();
        }
    }

    private sealed class FakeAccessTokenValidator(string issuer) : ICloudflareAccessTokenValidator
    {
        public Task<ClaimsPrincipal?> ValidateAsync(string token, CancellationToken cancellationToken)
        {
            var subject = token switch
            {
                "waterflex-employee" => "employee-subject",
                "dealer-technician" => "dealer-subject",
                _ => null
            };
            if (subject is null)
            {
                return Task.FromResult<ClaimsPrincipal?>(null);
            }

            var identity = new ClaimsIdentity(
            [
                new Claim("iss", issuer),
                new Claim("sub", subject)
            ], "TestCloudflareAccess");
            return Task.FromResult<ClaimsPrincipal?>(new ClaimsPrincipal(identity));
        }
    }
}
