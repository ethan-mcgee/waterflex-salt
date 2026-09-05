using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WaterFlex.SaltMonitor.Domain.Security;
using WaterFlex.SaltMonitor.Provisioning;

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence;

public sealed class EfFactoryStationService(SaltMonitorDbContext dbContext, TimeProvider timeProvider) : IFactoryStationService
{
    public async Task<WaterFlex.SaltMonitor.Provisioning.FactoryStationEnrollmentGrant?> CreateGrantAsync(FactoryStationEnrollmentGrantRequest request, StaffActor administrator, CancellationToken cancellationToken = default)
    {
        if (administrator.Role != StaffRole.WaterFlexAdministrator || !TryValidateIdentity(request.PublicKey, request.Thumbprint, out var publicKey, out var thumbprint)) return null;
        var displayName = Normalize(request.DisplayName, 100);
        if (displayName.Length == 0) return null;
        var now = timeProvider.GetUtcNow();
        var secret = RandomNumberGenerator.GetBytes(32);
        var grant = new FactoryStationEnrollmentGrant
        {
            Id = Guid.NewGuid(), SecretHash = SHA256.HashData(secret), DisplayName = displayName,
            PublicKey = publicKey, Thumbprint = thumbprint, CreatedBy = administrator.UserId,
            IssuedAtUtc = now, ExpiresAtUtc = now.AddMinutes(5)
        };
        dbContext.FactoryStationEnrollmentGrants.Add(grant);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new WaterFlex.SaltMonitor.Provisioning.FactoryStationEnrollmentGrant($"{grant.Id:N}.{Base64Url(secret)}", grant.ExpiresAtUtc);
    }

    public async Task<FactoryStationSummary?> EnrollAsync(EnrollFactoryStationRequest request, CancellationToken cancellationToken = default)
    {
        if (!TryParseGrant(request.GrantToken, out var grantId, out var secret)
            || !TryValidateIdentity(request.PublicKey, request.Thumbprint, out var publicKey, out var thumbprint)) return null;
        var grant = await dbContext.FactoryStationEnrollmentGrants.SingleOrDefaultAsync(item => item.Id == grantId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (grant is null || grant.ConsumedAtUtc is not null || grant.ExpiresAtUtc <= now
            || !CryptographicOperations.FixedTimeEquals(grant.SecretHash, SHA256.HashData(secret))
            || !string.Equals(grant.PublicKey, publicKey, StringComparison.Ordinal)
            || !string.Equals(grant.Thumbprint, thumbprint, StringComparison.Ordinal)) return null;
        var existing = await dbContext.FactoryStations.SingleOrDefaultAsync(item => item.Thumbprint == thumbprint, cancellationToken);
        grant.ConsumedAtUtc = now;
        if (existing is not null)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return existing.RevokedAtUtc is null ? Map(existing) : null;
        }
        var station = new FactoryStation
        {
            Id = Guid.NewGuid(), DisplayName = grant.DisplayName, PublicKey = publicKey, Thumbprint = thumbprint,
            KeyProviderType = Normalize(request.KeyProviderType, 64), HelperVersion = Normalize(request.HelperVersion, 32),
            ProtocolVersion = Normalize(request.ProtocolVersion, 16), EnrolledAtUtc = now, LastSeenAtUtc = now
        };
        dbContext.FactoryStations.Add(station);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(station);
    }

    public async Task<IReadOnlyList<FactoryStationSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        await dbContext.FactoryStations.AsNoTracking().OrderBy(item => item.DisplayName).Select(item => new FactoryStationSummary(
            item.Id, item.DisplayName, item.Thumbprint, item.KeyProviderType, item.HelperVersion, item.ProtocolVersion,
            item.EnrolledAtUtc, item.LastSeenAtUtc, item.RevokedAtUtc)).ToListAsync(cancellationToken);

    public async Task<FactoryStationSummary?> GetAsync(Guid stationId, CancellationToken cancellationToken = default)
    {
        var item = await dbContext.FactoryStations.AsNoTracking().SingleOrDefaultAsync(station => station.Id == stationId, cancellationToken);
        return item is null ? null : Map(item);
    }

    public async Task<FactoryStationSummary?> RenameAsync(Guid stationId, RenameFactoryStationRequest request, StaffActor administrator, CancellationToken cancellationToken = default)
    {
        if (administrator.Role != StaffRole.WaterFlexAdministrator) return null;
        var name = Normalize(request.DisplayName, 100);
        var station = name.Length == 0 ? null : await dbContext.FactoryStations.SingleOrDefaultAsync(item => item.Id == stationId, cancellationToken);
        if (station is null) return null;
        station.DisplayName = name;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(station);
    }

    public async Task<FactoryStationSummary?> RevokeAsync(Guid stationId, StaffActor administrator, CancellationToken cancellationToken = default)
    {
        if (administrator.Role != StaffRole.WaterFlexAdministrator) return null;
        var station = await dbContext.FactoryStations.SingleOrDefaultAsync(item => item.Id == stationId, cancellationToken);
        if (station is null) return null;
        station.RevokedAtUtc ??= timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(station);
    }

    public async Task<Guid?> ValidateSignedRequestAsync(string stationId, string method, string path, string timestamp, string nonce, string signature, ReadOnlyMemory<byte> exactBody, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(stationId, out var id) || !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var unix)
            || !TryBase64Url(nonce, out var nonceBytes) || nonceBytes.Length != 16 || !TryBase64Url(signature, out var signatureBytes) || signatureBytes.Length != 64) return null;
        var now = timeProvider.GetUtcNow();
        var signedAt = DateTimeOffset.FromUnixTimeSeconds(unix);
        if ((now - signedAt).Duration() > TimeSpan.FromMinutes(5)) return null;
        var station = await dbContext.FactoryStations.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (station is null || station.RevokedAtUtc is not null || !TryBase64Url(station.PublicKey, out var key) || key.Length != 65 || key[0] != 4) return null;
        var bodyHash = Convert.ToHexStringLower(SHA256.HashData(exactBody.Span));
        var canonical = $"WF-STATION-V1\n{method.ToUpperInvariant()}\n{path}\n{timestamp}\n{nonce}\n{bodyHash}";
        try
        {
            using var ecdsa = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = key[1..33], Y = key[33..65] } });
            if (!ecdsa.VerifyData(Encoding.UTF8.GetBytes(canonical), signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) return null;
        }
        catch (CryptographicException)
        {
            return null;
        }
        if (await dbContext.FactoryStationReplayNonces.AsNoTracking().AnyAsync(item => item.FactoryStationId == id && item.Nonce == nonce, cancellationToken)) return null;
        dbContext.FactoryStationReplayNonces.RemoveRange(dbContext.FactoryStationReplayNonces.Where(item => item.ExpiresAtUtc <= now));
        dbContext.FactoryStationReplayNonces.Add(new FactoryStationReplayNonce { Id = Guid.NewGuid(), FactoryStationId = id, Nonce = nonce, UsedAtUtc = now, ExpiresAtUtc = now.AddHours(24) });
        station.LastSeenAtUtc = now;
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) { return null; }
        return id;
    }

    private static FactoryStationSummary Map(FactoryStation item) => new(item.Id, item.DisplayName, item.Thumbprint, item.KeyProviderType, item.HelperVersion, item.ProtocolVersion, item.EnrolledAtUtc, item.LastSeenAtUtc, item.RevokedAtUtc);
    private static string Normalize(string? value, int max) { var result = value?.Trim() ?? string.Empty; return result[..Math.Min(result.Length, max)]; }
    private static bool TryValidateIdentity(string? publicKeyValue, string? thumbprintValue, out string publicKey, out string thumbprint)
    {
        if (publicKeyValue is null || thumbprintValue is null) { publicKey = string.Empty; thumbprint = string.Empty; return false; }
        publicKey = publicKeyValue.Trim(); thumbprint = thumbprintValue.Trim().ToLowerInvariant();
        return TryBase64Url(publicKey, out var bytes) && bytes.Length == 65 && bytes[0] == 4
            && thumbprint.Length == 64 && thumbprint.All(char.IsAsciiHexDigit)
            && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(thumbprint), Encoding.ASCII.GetBytes(Convert.ToHexStringLower(SHA256.HashData(bytes))));
    }
    private static bool TryParseGrant(string? token, out Guid id, out byte[] secret)
    {
        id = Guid.Empty; secret = [];
        if (token is null) return false;
        var parts = token.Split('.', 2);
        return parts.Length == 2 && Guid.TryParseExact(parts[0], "N", out id) && TryBase64Url(parts[1], out secret) && secret.Length == 32;
    }
    private static bool TryBase64Url(string value, out byte[] bytes)
    {
        bytes = []; try { var text = value.Replace('-', '+').Replace('_', '/'); bytes = Convert.FromBase64String(text.PadRight((text.Length + 3) / 4 * 4, '=')); return true; } catch (FormatException) { return false; }
    }
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
