using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using WaterFlex.SaltMonitor.Provisioning;

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence;

/// <summary>
/// Redeems a factory flash-authorization token presented by the local, loopback-only factory
/// workstation helper, which has no staff session of its own. Verifies the presented secret
/// against its stored hash in fixed time, then consumes the token so it cannot be replayed —
/// the same single-use, expiring pattern <see cref="EfDeviceBootstrapActivationService"/> uses for
/// device bootstrap credentials.
/// </summary>
public sealed class EfFactoryFlashAuthorizationService(
    SaltMonitorDbContext dbContext,
    TimeProvider timeProvider) : IFactoryFlashAuthorizationService
{
    public async Task<FlashAuthorizationRedemption?> RedeemAsync(
        FlashAuthorizationVerificationRequest request,
        Guid stationId,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseToken(request.Token, out var credentialId, out var secret)
            || !IsSha256(request.BundleSha256))
        {
            return null;
        }

        var authorization = await dbContext.FactoryFlashAuthorizations
            .SingleOrDefaultAsync(candidate => candidate.CredentialId == credentialId, cancellationToken);
        if (authorization is null)
        {
            return null;
        }

        var presentedHash = SHA256.HashData(secret);
        if (!CryptographicOperations.FixedTimeEquals(authorization.SecretHash, presentedHash))
        {
            authorization.FailedAttemptCount += 1;
            await dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }

        var now = timeProvider.GetUtcNow();
        if (authorization.DeviceId != request.DeviceId
            || authorization.ConsumedAtUtc is not null
            || authorization.RevokedAtUtc is not null
            || authorization.ExpiresAtUtc <= now)
        {
            return null;
        }

        var job = await dbContext.FactoryProvisioningJobs
            .Include(candidate => candidate.Device)
            .SingleOrDefaultAsync(candidate => candidate.Id == authorization.FactoryProvisioningJobId, cancellationToken);
        if (job is null
            || job.Status != FactoryProvisioningStatus.Registered
            || !string.Equals(job.IdempotencyKey, request.IdempotencyKey.Trim(), StringComparison.Ordinal)
            || !string.Equals(job.Device.FactoryFirmwareVersion, request.FirmwareVersion.Trim(), StringComparison.Ordinal)
            || !string.Equals(job.Device.FactoryConfigurationVersion, request.ConfigurationVersion.Trim(), StringComparison.Ordinal))
        {
            return null;
        }

        authorization.ConsumedAtUtc = now;
        authorization.RedeemedByFactoryStationId = stationId;
        var secretForVerification = RandomNumberGenerator.GetBytes(32);
        var verification = new FactoryVerificationAuthorization
        {
            Id = Guid.NewGuid(),
            FactoryProvisioningJobId = job.Id,
            DeviceId = job.DeviceId,
            FactoryStationId = stationId,
            CredentialId = $"wf_verify_{Guid.NewGuid():N}",
            SecretHash = SHA256.HashData(secretForVerification),
            FirmwareVersion = job.Device.FactoryFirmwareVersion!,
            ConfigurationVersion = job.Device.FactoryConfigurationVersion!,
            BundleSha256 = request.BundleSha256.Trim().ToLowerInvariant(),
            IssuedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(15)
        };
        dbContext.FactoryVerificationAuthorizations.Add(verification);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new FlashAuthorizationRedemption($"{verification.CredentialId}.{Base64Url(secretForVerification)}");
    }

    private static bool TryParseToken(string token, out string credentialId, out byte[] secret)
    {
        credentialId = string.Empty;
        secret = [];

        var separatorIndex = token.IndexOf('.');
        if (separatorIndex is <= 0 || separatorIndex == token.Length - 1)
        {
            return false;
        }

        credentialId = token[..separatorIndex].Trim();
        return credentialId.Length <= 64 && TryDecodeBase64UrlSecret(token[(separatorIndex + 1)..].Trim(), out secret);
    }

    private static bool TryDecodeBase64UrlSecret(string value, out byte[] secret)
    {
        secret = [];
        if (value.Length is < 42 or > 44)
        {
            return false;
        }

        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');

        try
        {
            secret = Convert.FromBase64String(base64);
            return secret.Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsSha256(string value) => value.Trim().Length == 64
        && value.Trim().All(character => char.IsAsciiHexDigit(character));

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
