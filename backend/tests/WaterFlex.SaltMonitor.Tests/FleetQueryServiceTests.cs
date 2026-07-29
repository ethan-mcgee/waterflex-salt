using Microsoft.EntityFrameworkCore;
using WaterFlex.SaltMonitor.Domain.Monitoring;
using WaterFlex.SaltMonitor.Infrastructure.Persistence;
using WaterFlex.SaltMonitor.Operations;
using Xunit;

namespace WaterFlex.SaltMonitor.Tests;

public sealed class FleetQueryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly MonitoringSchedule Schedule = new(TimeSpan.FromHours(2));

    [Fact]
    public async Task FleetQueries_SelectLatestReadingAndClassifyReportingState()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedFleetAsync(database.Context);
        var service = new EfFleetQueryService(database.Context, new FixedTimeProvider(Now), Schedule);

        var summary = await service.GetSummaryAsync(new());
        var page = await service.SearchAsync(new(new(), PageSize: 10));
        var lowDevice = Assert.Single(page.Items, item => item.SerialNumber == "WF-FLEET-REPORTING");

        Assert.Equal(4, summary.TotalProvisioned);
        Assert.Equal(1, summary.Reporting);
        Assert.Equal(1, summary.Stale);
        Assert.Equal(1, summary.Offline);
        Assert.Equal(1, summary.NeverReported);
        Assert.Equal(1, summary.BelowThreshold);
        Assert.Equal(20, lowDevice.FillPercent);
        Assert.True(lowDevice.IsBelowThreshold);
        Assert.Equal(DeviceReportingStatus.Reporting, lowDevice.ReportingStatus);
        Assert.Equal("North Star Water Systems", lowDevice.DealerName);
    }

    [Fact]
    public async Task Search_FiltersReportingAndFreeText()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedFleetAsync(database.Context);
        var service = new EfFleetQueryService(database.Context, new FixedTimeProvider(Now), Schedule);

        var page = await service.SearchAsync(new(new(
            Search: "OFFLINE",
            ReportingStatus: DeviceReportingStatus.Offline)));

        var device = Assert.Single(page.Items);
        Assert.Equal("WF-FLEET-OFFLINE", device.SerialNumber);
    }

    private static async Task SeedFleetAsync(SaltMonitorDbContext context)
    {
        var dealer = new Dealer
        {
            Id = Guid.NewGuid(),
            ExternalId = "WF-D-NORTH-STAR",
            DisplayName = "North Star Water Systems",
            IsActive = true
        };
        var customer = new CustomerAccount
        {
            Id = Guid.NewGuid(),
            WaterFlexCustomerId = "WF-C-FLEET",
            AccountNumber = "90001",
            DisplayName = "Fleet Test Customer",
            IsActive = true,
            LastSyncedAtUtc = Now
        };
        var location = new ServiceLocation
        {
            Id = Guid.NewGuid(),
            CustomerAccountId = customer.Id,
            WaterFlexLocationId = "WF-L-FLEET",
            DisplayName = "Fleet utility room",
            AddressSummary = "100 Test Way",
            IsActive = true,
            LastSyncedAtUtc = Now
        };
        context.AddRange(dealer, customer, location);

        var reporting = AddInstallation(context, dealer, location, "REPORTING");
        var stale = AddInstallation(context, dealer, location, "STALE");
        var offline = AddInstallation(context, dealer, location, "OFFLINE");
        AddInstallation(context, dealer, location, "NEVER");
        await context.SaveChangesAsync();

        AddReading(context, reporting, 1, Now.AddHours(-3), 70);
        AddReading(context, reporting, 2, Now.AddHours(-2), 20);
        AddReading(context, stale, 1, Now.AddHours(-6), 55);
        AddReading(context, offline, 1, Now.AddHours(-10), 60);
        await context.SaveChangesAsync();
    }

    private static (Device Device, DeviceInstallation Installation, TankCalibrationRecord Calibration)
        AddInstallation(
            SaltMonitorDbContext context,
            Dealer dealer,
            ServiceLocation location,
            string suffix)
    {
        var tank = new Tank
        {
            Id = Guid.NewGuid(),
            ServiceLocationId = location.Id,
            WaterFlexAssetId = $"WF-A-{suffix}",
            Label = $"{suffix} tank",
            IsActive = true
        };
        var device = new Device
        {
            Id = Guid.NewGuid(),
            SerialNumber = $"WF-FLEET-{suffix}",
            HardwareId = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            Model = "Arduino Nano ESP32",
            Status = DeviceLifecycleStatus.Active,
            RegisteredAtUtc = Now,
            CommissionedAtUtc = Now
        };
        var installation = new DeviceInstallation
        {
            Id = Guid.NewGuid(),
            DeviceId = device.Id,
            TankId = tank.Id,
            DealerId = dealer.Id,
            InstalledAtUtc = Now,
            InstalledBy = "Jordan Lee"
        };
        var calibration = new TankCalibrationRecord
        {
            Id = Guid.NewGuid(),
            DeviceInstallationId = installation.Id,
            Version = 1,
            TankDepthMm = 1500,
            CommissioningDistanceMm = 500,
            EffectiveFromUtc = Now,
            CreatedBy = "Jordan Lee",
            CreatedAtUtc = Now
        };
        context.AddRange(tank, device, installation, calibration);
        return (device, installation, calibration);
    }

    private static void AddReading(
        SaltMonitorDbContext context,
        (Device Device, DeviceInstallation Installation, TankCalibrationRecord Calibration) subject,
        long sequence,
        DateTimeOffset receivedAt,
        double fillPercent) =>
        context.TelemetryReadings.Add(new()
        {
            DeviceId = subject.Device.Id,
            DeviceInstallationId = subject.Installation.Id,
            TankCalibrationRecordId = subject.Calibration.Id,
            BootId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            SequenceNumber = sequence,
            ReceivedAtUtc = receivedAt,
            UptimeMilliseconds = sequence * 1000,
            RawDistanceMm = 1000,
            FillPercent = fillPercent,
            Quality = 90,
            SampleCount = 8,
            WifiRssiDbm = -60,
            FirmwareVersion = "1.0.0",
            ErrorFlagsJson = "[]"
        });

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
            var databaseName = $"WaterFlexFleetQueryTests_{Guid.NewGuid():N}";
            var connectionString = $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True";
            var options = new DbContextOptionsBuilder<SaltMonitorDbContext>()
                .UseSqlServer(connectionString)
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