using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WaterFlex.SaltMonitor.Infrastructure.Persistence;
using Xunit;

namespace WaterFlex.SaltMonitor.Tests;

public sealed class OpsApiTests
{
    [Fact]
    public async Task ReadingsEndpoint_ReturnsNotFoundForMissingDevice()
    {
        await using var factory = new OpsApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-WaterFlex-Development-User", "wf-ops-alex");

        var response = await client.GetAsync(
            $"/api/v1/ops/devices/{Guid.NewGuid()}/readings?range=24h&limit=50");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public async Task ReadingsEndpoint_RejectsLimitOutsideAllowedRange(int limit)
    {
        await using var factory = new OpsApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-WaterFlex-Development-User", "wf-ops-alex");

        var response = await client.GetAsync(
            $"/api/v1/ops/devices/{Guid.NewGuid()}/readings?range=24h&limit={limit}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Limit must be between 1 and 500", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task HistoryEndpoint_RejectsUnknownResolution()
    {
        await using var factory = new OpsApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-WaterFlex-Development-User", "wf-ops-alex");

        var response = await client.GetAsync(
            $"/api/v1/ops/devices/{Guid.NewGuid()}/history?range=7d&resolution=minute");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Resolution must be one of auto, hour, or day", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task HistoryEndpoint_EmitsCacheValidatorAndHonorsConditionalRequest()
    {
        await using var factory = new OpsApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-WaterFlex-Development-User", "wf-ops-alex");
        var deviceId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SaltMonitorDbContext>();
            context.Devices.Add(new()
            {
                Id = deviceId,
                SerialNumber = $"WF-CACHE-{deviceId:N}",
                HardwareId = deviceId.ToString("N"),
                Model = "Nano ESP32",
                Status = DeviceLifecycleStatus.Active,
                RegisteredAtUtc = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        var first = await client.GetAsync($"/api/v1/ops/devices/{deviceId}/history?range=7d&resolution=auto");
        var entityTag = first.Headers.ETag;
        Assert.NotNull(entityTag);
        using var conditional = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/ops/devices/{deviceId}/history?range=7d&resolution=auto");
        conditional.Headers.IfNoneMatch.Add(entityTag);
        var second = await client.SendAsync(conditional);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(60), first.Headers.CacheControl?.MaxAge);
        Assert.True(first.Headers.CacheControl?.Private);
        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    private sealed class OpsApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:SaltMonitor"] = TestPostgres.GetConnectionString(
                        $"WaterFlexOpsApiTests_{Guid.NewGuid():N}")
                }));
        }
    }
}
