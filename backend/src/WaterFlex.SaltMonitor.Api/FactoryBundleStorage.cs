using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text.Json;

namespace WaterFlex.SaltMonitor.Api;

/// <summary>A presigned download for the factory firmware bundle currently approved by <see cref="FactoryProvisioningOptions"/>.</summary>
public sealed record FactoryBundleLocation(string DownloadUrl, string Sha256, DateTimeOffset ExpiresAtUtc);

/// <summary>Resolves the approved factory firmware bundle to a time-limited, presigned S3 download.</summary>
public interface IFactoryBundleStorage
{
    Task<FactoryBundleLocation?> ResolveAsync(
        string firmwareVersion,
        string configurationVersion,
        CancellationToken cancellationToken);
}

/// <summary>
/// Locates the bundle object at a key derived from the approved firmware/configuration version, so this
/// stays automatically in lockstep with what <c>GET /api/v1/factory/configuration</c> already advertises
/// as approved, with no separate pointer to drift out of sync.
/// </summary>
public sealed class FactoryBundleStorage(
    IAmazonS3 s3,
    IOptions<FactoryProvisioningOptions> configured) : IFactoryBundleStorage
{
    private static readonly TimeSpan UrlLifetime = TimeSpan.FromMinutes(15);

    public async Task<FactoryBundleLocation?> ResolveAsync(
        string firmwareVersion,
        string configurationVersion,
        CancellationToken cancellationToken)
    {
        var options = configured.Value;
        if (string.IsNullOrWhiteSpace(options.BundleBucket) || string.IsNullOrWhiteSpace(options.BundleKeyPrefix))
        {
            return null;
        }

        var prefix = $"{options.BundleKeyPrefix.TrimEnd('/')}/{firmwareVersion}/{configurationVersion}";
        var key = $"{prefix}/waterflex-factory.bin";
        var manifestKey = $"{prefix}/factory-bundle.json";

        FactoryBundleManifest manifest;
        try
        {
            using var response = await s3.GetObjectAsync(options.BundleBucket, manifestKey, cancellationToken);
            using var memory = new MemoryStream();
            await response.ResponseStream.CopyToAsync(memory, cancellationToken);
            if (memory.Length is < 1 or > 32_768) throw new InvalidOperationException("Factory bundle manifest size is invalid.");
            var manifestDigest = response.Metadata["sha256"];
            if (string.IsNullOrWhiteSpace(manifestDigest)
                || !string.Equals(manifestDigest, Convert.ToHexStringLower(SHA256.HashData(memory.ToArray())), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Factory bundle manifest digest is invalid.");
            manifest = JsonSerializer.Deserialize<FactoryBundleManifest>(memory.ToArray(), new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException("Factory bundle manifest is invalid.");
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        GetObjectMetadataResponse metadata;
        try
        {
            metadata = await s3.GetObjectMetadataAsync(options.BundleBucket, key, cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        var sha256 = metadata.Metadata["sha256"];
        if (string.IsNullOrWhiteSpace(sha256))
        {
            throw new InvalidOperationException($"Factory bundle object '{key}' is missing required sha256 metadata.");
        }
        if (!string.Equals(manifest.FirmwareVersion, firmwareVersion, StringComparison.Ordinal)
            || !string.Equals(manifest.ConfigurationVersion, configurationVersion, StringComparison.Ordinal)
            || !string.Equals(manifest.MergedImage.File, "waterflex-factory.bin", StringComparison.Ordinal)
            || !string.Equals(manifest.MergedImage.Sha256, sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Factory bundle manifest is incompatible with the approved binary.");

        var expiresAtUtc = DateTimeOffset.UtcNow.Add(UrlLifetime);
        var downloadUrl = await s3.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = options.BundleBucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = expiresAtUtc.UtcDateTime
        });

        return new FactoryBundleLocation(downloadUrl, sha256, expiresAtUtc);
    }

    private sealed record FactoryBundleManifest(string FirmwareVersion, string ConfigurationVersion, FactoryBundleImage MergedImage);
    private sealed record FactoryBundleImage(string File, string Sha256);
}
