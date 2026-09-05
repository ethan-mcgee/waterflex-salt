using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WaterFlex.SaltMonitor.Domain.Security;
using WaterFlex.SaltMonitor.Infrastructure.Persistence;
using WaterFlex.SaltMonitor.Provisioning;
using Xunit;

namespace WaterFlex.SaltMonitor.Tests;

public sealed class FactoryStationServiceTests
{
    [Fact]
    public async Task EnrollmentSignatureReplayExpiryAndRevocationAreEnforced()
    {
        var options = new DbContextOptionsBuilder<SaltMonitorDbContext>().UseNpgsql(TestPostgres.GetConnectionString($"FactoryStations_{Guid.NewGuid():N}")).Options;
        await using var context = new SaltMonitorDbContext(options);
        await context.Database.MigrateAsync();
        try
        {
            var now = new MutableTimeProvider(new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero));
            var service = new EfFactoryStationService(context, now);
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var parameters = key.ExportParameters(false); var raw = new byte[65]; raw[0] = 4;
            parameters.Q.X!.CopyTo(raw, 1); parameters.Q.Y!.CopyTo(raw, 33);
            var publicKey = Base64Url(raw); var thumbprint = Convert.ToHexStringLower(SHA256.HashData(raw));
            var admin = new StaffActor("admin", "Admin", StaffRole.WaterFlexAdministrator, null, null);
            var grant = await service.CreateGrantAsync(new("Station A", publicKey, thumbprint), admin);
            Assert.NotNull(grant);
            var enrolled = await service.EnrollAsync(new(grant!.GrantToken, "ignored", publicKey, thumbprint, "tpm", "4.0.0", "4"));
            Assert.NotNull(enrolled);
            Assert.Null(await service.EnrollAsync(new(grant.GrantToken, "Station A", publicKey, thumbprint, "tpm", "4.0.0", "4")));

            var body = Encoding.UTF8.GetBytes("{\"test\":true}"); var nonce = Base64Url(RandomNumberGenerator.GetBytes(16));
            var timestamp = now.GetUtcNow().ToUnixTimeSeconds().ToString(); const string path = "/api/v1/factory/verifications";
            var canonical = $"WF-STATION-V1\nPOST\n{path}\n{timestamp}\n{nonce}\n{Convert.ToHexStringLower(SHA256.HashData(body))}";
            var signature = Base64Url(key.SignData(Encoding.UTF8.GetBytes(canonical), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
            Assert.Equal(enrolled!.StationId, await service.ValidateSignedRequestAsync(enrolled.StationId.ToString(), "POST", path, timestamp, nonce, signature, body));
            Assert.Null(await service.ValidateSignedRequestAsync(enrolled.StationId.ToString(), "POST", path, timestamp, nonce, signature, body));
            await service.RevokeAsync(enrolled.StationId, admin);
            Assert.Null(await service.ValidateSignedRequestAsync(enrolled.StationId.ToString(), "POST", path, timestamp, Base64Url(RandomNumberGenerator.GetBytes(16)), signature, body));

            var secondKey = NewPublicKey();
            var expiringGrant = await service.CreateGrantAsync(new("Station B", Base64Url(secondKey), Convert.ToHexStringLower(SHA256.HashData(secondKey))), admin);
            Assert.NotNull(expiringGrant);
            now.Advance(TimeSpan.FromMinutes(6));
            Assert.Null(await service.EnrollAsync(new(expiringGrant!.GrantToken, "Station B", Base64Url(secondKey), Convert.ToHexStringLower(SHA256.HashData(secondKey)), "software", "4.0.0", "4")));
        }
        finally { await context.Database.EnsureDeletedAsync(); }
    }

    private static byte[] NewPublicKey() { using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); var p = key.ExportParameters(false); var raw = new byte[65]; raw[0] = 4; p.Q.X!.CopyTo(raw, 1); p.Q.Y!.CopyTo(raw, 33); return raw; }
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private sealed class MutableTimeProvider(DateTimeOffset value) : TimeProvider { public override DateTimeOffset GetUtcNow() => value; public void Advance(TimeSpan duration) => value = value.Add(duration); }
}
