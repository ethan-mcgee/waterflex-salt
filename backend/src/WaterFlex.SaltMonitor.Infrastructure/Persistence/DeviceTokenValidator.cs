using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using WaterFlex.SaltMonitor.Ingestion;

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence;

/// <summary>
/// Validates a device's bearer token (credential ID + secret) presented on ingestion/health
/// calls, checking the secret against its stored hash in fixed time and rejecting revoked,
/// not-yet-valid, expired, or not-Active-device credentials before any request is allowed through.
/// </summary>
public sealed class DeviceTokenValidator(
    SaltMonitorDbContext dbContext,
    TimeProvider timeProvider) : IDeviceTokenValidator
{
    public async Task<DeviceTokenValidationResult> ValidateAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var separatorIndex = token.IndexOf('.');
        if (separatorIndex is <= 0 || separatorIndex == token.Length - 1)
        {
            return DeviceTokenValidationResult.Failed(DeviceTokenFailure.Invalid);
        }

        var credentialId = token[..separatorIndex];
        if (credentialId.Length > 64 || !TryDecodeSecret(token[(separatorIndex + 1)..], out var secret))
        {
            return DeviceTokenValidationResult.Failed(DeviceTokenFailure.Invalid);
        }

        var credential = await dbContext.DeviceCredentials
            .Include(candidate => candidate.Device)
            .SingleOrDefaultAsync(
                candidate => candidate.CredentialId == credentialId,
                cancellationToken);

        if (credential is null)
        {
            return DeviceTokenValidationResult.Failed(DeviceTokenFailure.Invalid);
        }

        var presentedHash = SHA256.HashData(secret);
        if (credential.SecretHash.Length != presentedHash.Length
            || !CryptographicOperations.FixedTimeEquals(credential.SecretHash, presentedHash))
        {
            return DeviceTokenValidationResult.Failed(DeviceTokenFailure.Invalid);
        }

        var now = timeProvider.GetUtcNow();
        if (credential.RevokedAtUtc is not null)
        {
            return DeviceTokenValidationResult.Failed(DeviceTokenFailure.Revoked);
        }

        if (credential.ValidFromUtc > now || credential.ExpiresAtUtc <= now)
        {
            return DeviceTokenValidationResult.Failed(DeviceTokenFailure.Expired);
        }

        if (credential.Device.Status is not DeviceLifecycleStatus.Active)
        {
            return DeviceTokenValidationResult.Failed(DeviceTokenFailure.DeviceUnavailable);
        }

        return DeviceTokenValidationResult.Valid(credential.DeviceId, credential.Id);
    }

    private static bool TryDecodeSecret(string value, out byte[] secret)
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

/// <summary>Stamps a device credential's last-used timestamp via a direct update, avoiding the overhead of loading and tracking the full entity on every authenticated request.</summary>
public sealed class DeviceCredentialUsageRecorder(
    SaltMonitorDbContext dbContext,
    TimeProvider timeProvider) : IDeviceCredentialUsageRecorder
{
    public Task RecordAsync(Guid credentialRecordId, CancellationToken cancellationToken = default) =>
        dbContext.DeviceCredentials
            .Where(credential => credential.Id == credentialRecordId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    credential => credential.LastUsedAtUtc,
                    timeProvider.GetUtcNow()),
                cancellationToken);
}
