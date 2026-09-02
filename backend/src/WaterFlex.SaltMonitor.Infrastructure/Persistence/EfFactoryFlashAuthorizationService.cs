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
    public async Task<bool> VerifyAsync(
        Guid deviceId,
        string token,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseToken(token, out var credentialId, out var secret))
        {
            return false;
        }

        var authorization = await dbContext.FactoryFlashAuthorizations
            .SingleOrDefaultAsync(candidate => candidate.CredentialId == credentialId, cancellationToken);
        if (authorization is null)
        {
            return false;
        }

        var presentedHash = SHA256.HashData(secret);
        if (!CryptographicOperations.FixedTimeEquals(authorization.SecretHash, presentedHash))
        {
            authorization.FailedAttemptCount += 1;
            await dbContext.SaveChangesAsync(cancellationToken);
            return false;
        }

        var now = timeProvider.GetUtcNow();
        if (authorization.DeviceId != deviceId
            || authorization.ConsumedAtUtc is not null
            || authorization.RevokedAtUtc is not null
            || authorization.ExpiresAtUtc <= now)
        {
            return false;
        }

        authorization.ConsumedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
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
}
