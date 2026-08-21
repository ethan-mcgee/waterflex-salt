using System.Net;
using System.Security.Claims;
using System.Net.Http.Json;
using System.Text.Json;
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

    [Fact]
    public async Task WaterFlexAdministrator_InheritsFleetAccessAndCanListStaff()
    {
        await using var factory = new StaffApiFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add(StaffAuthenticationHandler.AccessAssertionHeader, "waterflex-administrator");

        var fleetResponse = await client.GetAsync("/api/v1/ops/dealers");
        var staffResponse = await client.GetAsync("/api/v1/staff-admin/staff");

        Assert.Equal(HttpStatusCode.OK, fleetResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, staffResponse.StatusCode);
    }

    [Fact]
    public async Task DealerAdministrator_CannotAccessWaterFlexFleetButCanListOwnDealerStaff()
    {
        await using var factory = new StaffApiFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add(StaffAuthenticationHandler.AccessAssertionHeader, "dealer-administrator");

        var fleetResponse = await client.GetAsync("/api/v1/ops/dealers");
        var staffResponse = await client.GetAsync("/api/v1/staff-admin/staff");

        Assert.Equal(HttpStatusCode.Forbidden, fleetResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, staffResponse.StatusCode);
        var body = await staffResponse.Content.ReadAsStringAsync();
        Assert.Contains("technician@example.test", body);
        Assert.DoesNotContain("operator@example.test", body);
    }

    [Fact]
    public async Task WaterFlexAdministrator_CreatesInvitationAuditAndOutboxAtomically()
    {
        await using var factory = new StaffApiFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add(StaffAuthenticationHandler.AccessAssertionHeader, "waterflex-administrator");
        client.DefaultRequestHeaders.Add("X-WaterFlex-Request", "console");

        var response = await client.PostAsJsonAsync("/api/v1/staff-admin/invitations", new
        {
            email = "new.employee@example.test", displayName = "New Employee",
            role = "waterFlexEmployee", dealerExternalId = (string?)null, reason = "Pilot operations coverage"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<SaltMonitorDbContext>();
        Assert.Equal(1, await database.StaffInvitations.CountAsync(item => item.NormalizedEmail == "NEW.EMPLOYEE@EXAMPLE.TEST"));
        Assert.Equal(1, await database.StaffProvisioningWorkItems.CountAsync(item => item.WorkType == "ProvisionInvitation"));
        Assert.Equal(1, await database.StaffAccessAuditEvents.CountAsync(item => item.EventType == "staff.invitation.created"));
    }

    [Fact]
    public async Task LastWaterFlexAdministrator_CannotBeSuspended()
    {
        await using var factory = new StaffApiFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add(StaffAuthenticationHandler.AccessAssertionHeader, "waterflex-administrator");
        client.DefaultRequestHeaders.Add("X-WaterFlex-Request", "console");
        var staff = JsonDocument.Parse(await (await client.GetAsync("/api/v1/staff-admin/staff")).Content.ReadAsStringAsync());
        var administrator = staff.RootElement.EnumerateArray().Single(item => item.GetProperty("role").GetString() == "waterFlexAdministrator");

        var response = await client.PostAsJsonAsync($"/api/v1/staff-admin/staff/{administrator.GetProperty("id").GetGuid()}/suspend", new
        {
            reason = "Should be rejected", rowVersion = administrator.GetProperty("rowVersion").GetUInt32()
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
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
                    NormalizedEmail = "OPERATOR@EXAMPLE.TEST",
                    DisplayName = "Pilot Operator",
                    Role = StaffRole.WaterFlexEmployee,
                    IsActive = true,
                    State = StaffIdentityState.Active,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                },
                new StaffIdentityRecord
                {
                    Id = Guid.NewGuid(),
                    Issuer = Issuer,
                    Subject = "dealer-subject",
                    Email = "technician@example.test",
                    NormalizedEmail = "TECHNICIAN@EXAMPLE.TEST",
                    DisplayName = "Pilot Technician",
                    Role = StaffRole.DealerTechnician,
                    DealerId = dealer.Id,
                    IsActive = true,
                    State = StaffIdentityState.Active,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                },
                new StaffIdentityRecord
                {
                    Id = Guid.NewGuid(), Issuer = Issuer, Subject = "administrator-subject",
                    Email = "administrator@example.test", NormalizedEmail = "ADMINISTRATOR@EXAMPLE.TEST",
                    DisplayName = "WaterFlex Administrator", Role = StaffRole.WaterFlexAdministrator,
                    IsActive = true, State = StaffIdentityState.Active,
                    CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow
                },
                new StaffIdentityRecord
                {
                    Id = Guid.NewGuid(), Issuer = Issuer, Subject = "dealer-administrator-subject",
                    Email = "dealer.admin@example.test", NormalizedEmail = "DEALER.ADMIN@EXAMPLE.TEST",
                    DisplayName = "Dealer Administrator", Role = StaffRole.DealerAdministrator, DealerId = dealer.Id,
                    IsActive = true, State = StaffIdentityState.Active,
                    CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow
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
                "waterflex-administrator" => "administrator-subject",
                "dealer-administrator" => "dealer-administrator-subject",
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
