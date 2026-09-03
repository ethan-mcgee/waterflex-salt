using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using System.Reflection;
using WaterFlex.SaltMonitor.Api;
using Xunit;

namespace WaterFlex.SaltMonitor.Tests;

public sealed class FactoryBundleStorageTests
{
    [Fact]
    public async Task ResolveAsync_AcceptsS3UserMetadataKeyShape()
    {
        const string expectedSha256 = "41eb955ea6010c4798f69e584eaeed0dee181ed648046f5a6e76a691196b75cc";
        var s3 = DispatchProxy.Create<IAmazonS3, StubS3Proxy>();
        ((StubS3Proxy)(object)s3).Sha256 = expectedSha256;
        var storage = new FactoryBundleStorage(
            s3,
            Options.Create(new FactoryProvisioningOptions
            {
                BundleBucket = "waterflex-test-bucket",
                BundleKeyPrefix = "factory-bundles"
            }));

        var location = await storage.ResolveAsync("firmware-v1", "configuration-v1", CancellationToken.None);

        Assert.NotNull(location);
        Assert.Equal(expectedSha256, location!.Sha256);
        Assert.Equal("https://example.test/presigned", location.DownloadUrl);
    }

    private class StubS3Proxy : DispatchProxy
    {
        public string Sha256 { get; set; } = string.Empty;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IAmazonS3.GetObjectMetadataAsync))
            {
                var response = new GetObjectMetadataResponse();
                response.Metadata.Add("sha256", Sha256);

            Assert.Contains("x-amz-meta-sha256", response.Metadata.Keys);
                return Task.FromResult(response);
            }

            if (targetMethod?.Name == nameof(IAmazonS3.GetPreSignedURLAsync))
            {
                return Task.FromResult("https://example.test/presigned");
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }
}
