using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using WaterFlex.SaltMonitor.Infrastructure.Persistence;
using WaterFlex.SaltMonitor.Ingestion;
using Xunit;

namespace WaterFlex.SaltMonitor.Tests;

public sealed class TelemetryPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task IngestAsync_PersistsThenAcknowledgesDuplicate()
    {
        await using var database = await TestDatabase.CreateAsync();
        var device = await SeedCommissionedDeviceAsync(database.Context);
        var validator = new TelemetryBatchValidator(new FixedTimeProvider(Now));
        var service = new EfTelemetryIngestionService(database.Context, validator, new FixedTimeProvider(Now));
        var reading = new TelemetryReadingInput(
            Guid.NewGuid(), 1, Now, 1000, 750, 90, 8, -60);
        var batch = new TelemetryBatch(1, "1.0.0", [reading]);

        var accepted = await service.IngestAsync(device.Id, batch);
        var duplicate = await service.IngestAsync(device.Id, batch);

        Assert.True(accepted.IsSuccess);
        Assert.Equal(TelemetryReadingStatus.Accepted, accepted.Acknowledgement!.Readings.Single().Status);
        Assert.True(duplicate.IsSuccess);
        Assert.Equal(TelemetryReadingStatus.Duplicate, duplicate.Acknowledgement!.Readings.Single().Status);
        Assert.Equal(
            accepted.Acknowledgement.Readings.Single().ReadingId,
            duplicate.Acknowledgement.Readings.Single().ReadingId);
        Assert.Equal(50, accepted.Acknowledgement.Readings.Single().FillPercent, precision: 5);
        Assert.Equal(1, await database.Context.TelemetryReadings.CountAsync());
    }

    [Fact]
    public async Task DeviceTokenValidator_ValidatesUniqueSecret()
    {
        await using var database = await TestDatabase.CreateAsync();
        var device = await SeedCommissionedDeviceAsync(database.Context);
        var secret = RandomNumberGenerator.GetBytes(32);
        const string credentialId = "credential-1";
        database.Context.DeviceCredentials.Add(new()
        {
            Id = Guid.NewGuid(),
            DeviceId = device.Id,
            CredentialId = credentialId,
            SecretHash = SHA256.HashData(secret),
            ValidFromUtc = Now.AddMinutes(-1)
        });
        await database.Context.SaveChangesAsync();
        var validator = new DeviceTokenValidator(database.Context, new FixedTimeProvider(Now));
        var token = $"{credentialId}.{Base64UrlEncode(secret)}";

        var valid = await validator.ValidateAsync(token);
        var invalid = await validator.ValidateAsync($"{credentialId}.{Base64UrlEncode(RandomNumberGenerator.GetBytes(32))}");

        Assert.True(valid.IsValid);
        Assert.Equal(device.Id, valid.DeviceId);
        Assert.Equal(DeviceTokenFailure.Invalid, invalid.Failure);
    }

    private static async Task<Device> SeedCommissionedDeviceAsync(SaltMonitorDbContext context)
    {
        var customer = new CustomerAccount
        {
            Id = Guid.NewGuid(),
            WaterFlexCustomerId = $"customer-{Guid.NewGuid():N}",
            DisplayName = "Test Customer",
            IsActive = true,
            LastSyncedAtUtc = Now
        };
        var location = new ServiceLocation
        {
            Id = Guid.NewGuid(),
            CustomerAccountId = customer.Id,
            WaterFlexLocationId = $"location-{Guid.NewGuid():N}",
            DisplayName = "Test Location",
            IsActive = true,
            LastSyncedAtUtc = Now
        };
        var tank = new Tank
        {
            Id = Guid.NewGuid(),
            ServiceLocationId = location.Id,
            Label = "Softener 1",
            IsActive = true
        };
        var device = new Device
        {
            Id = Guid.NewGuid(),
            SerialNumber = $"WF-{Guid.NewGuid():N}",
            HardwareId = Guid.NewGuid().ToString("N")[..12],
            Model = "Nano ESP32",
            Status = DeviceLifecycleStatus.Active,
            RegisteredAtUtc = Now,
            CommissionedAtUtc = Now
        };
        var installation = new DeviceInstallation
        {
            Id = Guid.NewGuid(),
            DeviceId = device.Id,
            TankId = tank.Id,
            InstalledAtUtc = Now
        };
        var calibration = new TankCalibrationRecord
        {
            Id = Guid.NewGuid(),
            DeviceInstallationId = installation.Id,
            Version = 1,
            TankDepthMm = 1500,
            CommissioningDistanceMm = 500,
            EffectiveFromUtc = Now,
            CreatedBy = "test",
            CreatedAtUtc = Now
        };

        context.AddRange(customer, location, tank, device, installation, calibration);
        await context.SaveChangesAsync();
        return device;
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string _databaseName;

        private TestDatabase(string databaseName, SaltMonitorDbContext context)
        {
            _databaseName = databaseName;
            Context = context;
        }

        public SaltMonitorDbContext Context { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var databaseName = $"WaterFlexSaltMonitorTests_{Guid.NewGuid():N}";
            var connectionString = $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True";
            var options = new DbContextOptionsBuilder<SaltMonitorDbContext>()
                .UseSqlServer(connectionString)
                .Options;
            var context = new SaltMonitorDbContext(options);
            await context.Database.MigrateAsync();
            return new(databaseName, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.Database.EnsureDeletedAsync();
            await Context.DisposeAsync();
        }
    }
}