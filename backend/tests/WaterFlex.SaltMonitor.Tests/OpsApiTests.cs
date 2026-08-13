using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
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

    private sealed class OpsApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:SaltMonitor"] =
                        "Host=localhost;Port=5432;Database=WaterFlexOpsApiTests;Username=postgres;Password=postgres"
                }));
        }
    }
}
