using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WaterFlex.SaltMonitor.Api;
using Xunit;

namespace WaterFlex.SaltMonitor.Tests;

public sealed class FactoryBundleApiTests
{
    [Fact]
    public async Task DisabledFactoryProvisioning_ReturnsServiceUnavailable()
    {
        await using var factory = new FactoryBundleApiFactory(
            factoryProvisioningEnabled: false,
            bundleStorage: new StubFactoryBundleStorage(_ => throw new InvalidOperationException("Should not be reached.")));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/factory/bundle");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task StorageFailure_ReturnsInternalServerError()
    {
        await using var factory = new FactoryBundleApiFactory(
            bundleStorage: new StubFactoryBundleStorage(_ => throw new InvalidOperationException("S3 unavailable.")));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/factory/bundle");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task MissingBundleObject_ReturnsInternalServerError()
    {
        await using var factory = new FactoryBundleApiFactory(
            bundleStorage: new StubFactoryBundleStorage(_ => Task.FromResult<FactoryBundleLocation?>(null)));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/factory/bundle");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task ApprovedBundle_ReturnsPresignedDownloadMatchingCurrentConfiguration()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(15);
        await using var factory = new FactoryBundleApiFactory(
            bundleStorage: new StubFactoryBundleStorage(_ => Task.FromResult<FactoryBundleLocation?>(
                new FactoryBundleLocation("https://example-bucket.s3.amazonaws.com/signed", "deadbeef", expiresAt))));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/factory/bundle");
        var body = await response.Content.ReadFromJsonAsync<FactoryBundleDownload>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("wf-uart-pilot-0.1", body!.FirmwareVersion);
        Assert.Equal("factory-v2", body.ConfigurationVersion);
        Assert.Equal("https://example-bucket.s3.amazonaws.com/signed", body.DownloadUrl);
        Assert.Equal("deadbeef", body.Sha256);
    }

    private sealed class StubFactoryBundleStorage(
        Func<CancellationToken, Task<FactoryBundleLocation?>> resolve) : IFactoryBundleStorage
    {
        public Task<FactoryBundleLocation?> ResolveAsync(
            string firmwareVersion,
            string configurationVersion,
            CancellationToken cancellationToken) => resolve(cancellationToken);
    }

    private sealed class FactoryBundleApiFactory(
        IFactoryBundleStorage bundleStorage,
        bool factoryProvisioningEnabled = true) : WebApplicationFactory<Program>
    {
        private readonly string connectionString = TestPostgres.GetConnectionString(
            $"WaterFlexFactoryBundleApiTests_{Guid.NewGuid():N}");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:SaltMonitor"] = connectionString,
                    ["FactoryProvisioning:Enabled"] = factoryProvisioningEnabled.ToString()
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IFactoryBundleStorage>();
                services.AddSingleton(bundleStorage);
            });
        }
    }
}
