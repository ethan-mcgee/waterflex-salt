using Microsoft.EntityFrameworkCore;
using WaterFlex.SaltMonitor.Domain.Security;
using WaterFlex.SaltMonitor.Infrastructure.Persistence;
using WaterFlex.SaltMonitor.Ingestion;
using Xunit;

namespace WaterFlex.SaltMonitor.Tests;

public sealed class CommissioningServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly StaffActor Technician = new(
        "north-star-jordan",
        "Jordan Lee",
        StaffRole.DealerTechnician,
        "WF-D-NORTH-STAR",
        "North Star Water Systems");

    [Fact]
    public async Task CommissionAsync_CreatesCompleteInstallationAndValidToken()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);

        var result = await service.CommissionAsync(CreateRequest(), Technician);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Commissioning);
        Assert.Equal("WF-NANO-0001", result.Commissioning.SerialNumber);
        Assert.Equal("North Ridge Apartments", result.Commissioning.CustomerDisplayName);
        Assert.Equal(150m, result.Commissioning.TankDepthCm);
        Assert.Equal(50m, result.Commissioning.CommissioningDistanceCm);
        Assert.Equal(66.66667, result.Commissioning.InitialFillPercent, precision: 5);
        Assert.Equal(1, await database.Context.Devices.CountAsync());
        Assert.Equal(1, await database.Context.DeviceInstallations.CountAsync());
        Assert.Equal(1, await database.Context.TankCalibrations.CountAsync());
        Assert.Equal(1, await database.Context.DeviceCredentials.CountAsync());
        Assert.Equal(1, await database.Context.Dealers.CountAsync());
        var installation = await database.Context.DeviceInstallations.SingleAsync();
        Assert.NotNull(installation.DealerId);
        Assert.Equal("Jordan Lee", installation.InstalledBy);
        var calibration = await database.Context.TankCalibrations.SingleAsync();
        Assert.Equal(1500, calibration.TankDepthMm);
        Assert.Equal(500, calibration.CommissioningDistanceMm);

        var tokenValidator = new DeviceTokenValidator(database.Context, new FixedTimeProvider(Now));
        var tokenResult = await tokenValidator.ValidateAsync(result.Commissioning.DeviceToken);

        Assert.True(tokenResult.IsValid);
        Assert.Equal(result.Commissioning.DeviceId, tokenResult.DeviceId);
    }

    [Fact]
    public async Task CommissionAsync_RejectsDuplicateHardwareId()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var first = await service.CommissionAsync(CreateRequest(), Technician);

        var duplicate = await service.CommissionAsync(CreateRequest() with
        {
            SerialNumber = "WF-NANO-0002"
        }, Technician);

        Assert.True(first.IsSuccess);
        Assert.Equal(CommissioningFailure.DeviceAlreadyRegistered, duplicate.Failure);
        Assert.Equal(1, await database.Context.Devices.CountAsync());
    }

    [Fact]
    public async Task CommissionAsync_RejectsOccupiedTank()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var first = await service.CommissionAsync(CreateRequest(), Technician);

        var occupied = await service.CommissionAsync(CreateRequest() with
        {
            SerialNumber = "WF-NANO-0002",
            HardwareId = "A1B2C3D4E5F7"
        }, Technician);

        Assert.True(first.IsSuccess);
        Assert.Equal(CommissioningFailure.TankAlreadyOccupied, occupied.Failure);
        Assert.Equal(1, await database.Context.DeviceInstallations.CountAsync());
    }

    [Fact]
    public async Task CommissionAsync_RejectsMoreThanOneDecimalPlaceInCentimeters()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);

        var result = await service.CommissionAsync(CreateRequest() with
        {
            TankDepthCm = 150.01m
        }, Technician);

        Assert.Equal(CommissioningFailure.InvalidRequest, result.Failure);
        Assert.Contains(
            result.ValidationErrors,
            error => error.Field == nameof(CommissionSensorRequest.TankDepthCm));
        Assert.Equal(0, await database.Context.Devices.CountAsync());
    }

    private static EfSensorCommissioningService CreateService(SaltMonitorDbContext context) =>
        new(
            context,
            new DevelopmentWaterFlexCustomerDirectory(),
            new FixedTimeProvider(Now));

    private static CommissionSensorRequest CreateRequest() =>
        new(
            "WF-C-10482",
            "WF-L-10482-01",
            "WF-A-10482-S1",
            "wf-nano-0001",
            "A1:B2:C3:D4:E5:F6",
            "Arduino Nano ESP32",
            "WO-82417",
            150m,
            50m);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private TestDatabase(SaltMonitorDbContext context) => Context = context;

        public SaltMonitorDbContext Context { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var databaseName = $"WaterFlexCommissioningTests_{Guid.NewGuid():N}";
            var connectionString = await TestPostgres.GetConnectionStringAsync(databaseName);
            var options = new DbContextOptionsBuilder<SaltMonitorDbContext>()
                .UseNpgsql(connectionString)
                .Options;
            var context = new SaltMonitorDbContext(options);
            await context.Database.MigrateAsync();
            return new(context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.Database.EnsureDeletedAsync();
            await Context.DisposeAsync();
        }
    }
}
