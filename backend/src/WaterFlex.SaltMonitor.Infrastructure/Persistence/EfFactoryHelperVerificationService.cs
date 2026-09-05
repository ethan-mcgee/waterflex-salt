using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WaterFlex.SaltMonitor.Provisioning;

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence;

/// <summary>Persists end-of-line evidence submitted directly by the local helper after flash authorization redemption.</summary>
public sealed class EfFactoryHelperVerificationService(
    SaltMonitorDbContext dbContext,
    TimeProvider timeProvider) : IFactoryHelperVerificationService
{
    public async Task<FactoryVerificationResult?> RecordAsync(
        FactoryHelperVerificationRequest request,
        Guid stationId,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseToken(request.VerificationToken, out var credentialId, out var secret))
        {
            return null;
        }

        var authorization = await dbContext.FactoryVerificationAuthorizations
            .Include(candidate => candidate.Job)
            .ThenInclude(job => job.Device)
            .SingleOrDefaultAsync(candidate => candidate.CredentialId == credentialId, cancellationToken);
        if (authorization is null
            || !CryptographicOperations.FixedTimeEquals(authorization.SecretHash, SHA256.HashData(secret)))
        {
            return null;
        }

        var job = authorization.Job;
        if (authorization.FactoryStationId != stationId)
        {
            return null;
        }
        if (authorization.ResultJson is not null)
        {
            return JsonSerializer.Deserialize<FactoryVerificationResult>(authorization.ResultJson);
        }

        var now = timeProvider.GetUtcNow();
        if (authorization.DeviceId != request.DeviceId
            || authorization.ConsumedAtUtc is not null
            || authorization.RevokedAtUtc is not null
            || authorization.ExpiresAtUtc <= now
            || job.Status != FactoryProvisioningStatus.Registered
            || !string.Equals(job.IdempotencyKey, request.IdempotencyKey.Trim(), StringComparison.Ordinal)
            || !string.Equals(authorization.FirmwareVersion, request.FirmwareVersion.Trim(), StringComparison.Ordinal)
            || !string.Equals(authorization.ConfigurationVersion, request.ConfigurationVersion.Trim(), StringComparison.Ordinal)
            || !string.Equals(authorization.BundleSha256, request.BundleSha256.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var sensorEvidenceValid = request.SensorVerified && request.SensorSampleCount >= 5
            && request.SensorMinimumMm is >= 30 and <= 4500
            && request.SensorMaximumMm is >= 30 and <= 4500;
        var passed = request.FirmwareVerified && request.IdentityVerified
            && request.PortalVerified && request.PortalStartupObserved && sensorEvidenceValid;
        job.Status = passed ? FactoryProvisioningStatus.Provisioned : FactoryProvisioningStatus.Quarantined;
        job.VerifiedAtUtc = now;
        job.FailureCode = passed ? null : NormalizeFailureCode(request.FailureCode);
        authorization.ConsumedAtUtc = now;
        var result = new FactoryVerificationResult(request.DeviceId, job.SerialNumber, job.Status, now, job.FailureCode);
        authorization.ResultJson = JsonSerializer.Serialize(result);
        dbContext.ProvisioningAuditEvents.Add(new()
        {
            DeviceId = request.DeviceId,
            EventType = passed ? "factory_helper_verification_passed" : "factory_helper_verification_failed",
            ActorType = "factory_helper",
            ActorId = authorization.CredentialId,
            DetailsJson = JsonSerializer.Serialize(new
            {
                request.FirmwareVerified,
                request.IdentityVerified,
                request.PortalVerified,
                request.PortalStartupObserved,
                request.SensorVerified,
                request.SensorSampleCount,
                request.SensorMinimumMm,
                request.SensorMaximumMm,
                request.SensorFailureCategories,
                job.FailureCode,
                authorization.FirmwareVersion,
                authorization.ConfigurationVersion,
                authorization.BundleSha256
            }),
            OccurredAtUtc = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return result;
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
        if (credentialId.Length > 64)
        {
            return false;
        }
        var base64 = token[(separatorIndex + 1)..].Trim().Replace('-', '+').Replace('_', '/');
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

    private static string NormalizeFailureCode(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "factory_verification_failed" : value.Trim();
        return normalized[..Math.Min(100, normalized.Length)];
    }
}
