using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WaterFlex.SaltMonitor.Infrastructure.Persistence;
using Xunit;

namespace WaterFlex.SaltMonitor.Tests;

public sealed class TelemetryHistoryMaintenanceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 13, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Maintenance_RollsUpBeforeDeletingAndIsIdempotent()
    {
        await using var database = await TestDatabase.CreateAsync();
        var subject = await SeedSubjectAsync(database.Context);
        var oldDay = new DateTimeOffset(Now.AddDays(-40).Year, Now.AddDays(-40).Month, Now.AddDays(-40).Day, 0, 0, 0, TimeSpan.Zero);
        AddReading(database.Context, subject, 1, oldDay.AddHours(2).AddMinutes(5), 20, 80, -70, "[]");
        AddReading(database.Context, subject, 2, oldDay.AddHours(2).AddMinutes(55), 60, 55, -50, "[\"sensor_timeout\"]");
        AddReading(database.Context, subject, 3, Now.AddHours(-2), 75, 95, -45, "[]");
        await database.Context.SaveChangesAsync();
        var service = CreateService(database.Context);

        var first = await service.RunAsync();
        var second = await service.RunAsync();

        Assert.Equal(2, first.RawReadingsDeleted);
        Assert.Equal(0, second.RawReadingsDeleted);
        Assert.Single(await database.Context.TelemetryReadings.ToArrayAsync());
        var hourlyBucket = new DateTimeOffset(
            oldDay.Year,
            oldDay.Month,
            oldDay.Day,
            2,
            0,
            0,
            TimeSpan.Zero);
        var hourly = await database.Context.TelemetryHourlySummaries.SingleAsync(
            summary => summary.BucketStartUtc == hourlyBucket);
        Assert.Equal(1, hourly.ReadingCount);
        Assert.Equal(20, hourly.FillPercentMin);
        Assert.Equal(20, hourly.FillPercentMax);
        Assert.Equal(20, hourly.FillPercentAverage);
        Assert.Equal(20, hourly.FillPercentLatest);
        Assert.Equal(80, hourly.WorstQuality);
        Assert.Equal(0, hourly.ErrorCount);
        var daily = await database.Context.TelemetryDailySummaries.SingleAsync();
        Assert.Equal(1, daily.ReadingCount);
        Assert.Equal(hourly.FillPercentAverage, daily.FillPercentAverage);
        Assert.Single(await database.Context.TelemetryMaintenanceStates.ToArrayAsync());
    }

    [Fact]
    public async Task Maintenance_PreservesOldRawReadingUntilBothRollupsExist()
    {
        await using var database = await TestDatabase.CreateAsync();
        var subject = await SeedSubjectAsync(database.Context);
        var oldDay = Now.AddDays(-40);
        var receivedAt = new DateTimeOffset(oldDay.Year, oldDay.Month, oldDay.Day, 2, 0, 0, TimeSpan.Zero);
        AddReading(database.Context, subject, 1, receivedAt, 50, 90, -60, "[]");
        database.Context.TelemetryMaintenanceStates.Add(new()
        {
            Name = "telemetry-history-backfill-v2-operational-only",
            CompletedAtUtc = Now
        });
        await database.Context.SaveChangesAsync();

        var result = await CreateService(database.Context).RunAsync();

        Assert.Equal(0, result.RawReadingsDeleted);
        Assert.Single(await database.Context.TelemetryReadings.ToArrayAsync());
    }

    [Fact]
    public async Task RecentReadingQuery_CanUseCompositeHistoryIndex()
    {
        await using var database = await TestDatabase.CreateAsync();
        var subject = await SeedSubjectAsync(database.Context);
        AddReading(database.Context, subject, 1, Now.AddMinutes(-1), 50, 90, -60, "[]");
        await database.Context.SaveChangesAsync();
        await database.Context.Database.OpenConnectionAsync();
        try
        {
            await using (var disableSequentialScan = database.Context.Database.GetDbConnection().CreateCommand())
            {
                disableSequentialScan.CommandText = "SET enable_seqscan = off;";
                await disableSequentialScan.ExecuteNonQueryAsync();
            }

            await using var explain = database.Context.Database.GetDbConnection().CreateCommand();
            explain.CommandText = """
                EXPLAIN SELECT "Id"
                FROM "TelemetryReadings"
                WHERE "DeviceId" = @device_id AND "ReceivedAtUtc" >= @cutoff
                ORDER BY "ReceivedAtUtc" DESC, "Id" DESC
                LIMIT 50;
                """;
            var deviceParameter = explain.CreateParameter();
            deviceParameter.ParameterName = "device_id";
            deviceParameter.Value = subject.Device.Id;
            explain.Parameters.Add(deviceParameter);
            var cutoffParameter = explain.CreateParameter();
            cutoffParameter.ParameterName = "cutoff";
            cutoffParameter.Value = Now.AddHours(-24);
            explain.Parameters.Add(cutoffParameter);
            var plan = new List<string>();
            await using var reader = await explain.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                plan.Add(reader.GetString(0));
            }

            Assert.Contains(
                plan,
                line => line.Contains("IX_TelemetryReadings_DeviceId_ReceivedAtUtc_Id", StringComparison.Ordinal));
        }
        finally
        {
            await database.Context.Database.CloseConnectionAsync();
        }
    }

    [Fact]
    public async Task Maintenance_AllowsConcurrentRecentTelemetryInsert()
    {
        await using var database = await TestDatabase.CreateAsync();
        var subject = await SeedSubjectAsync(database.Context);
        var oldDay = Now.AddDays(-40);
        AddReading(
            database.Context,
            subject,
            1,
            new DateTimeOffset(oldDay.Year, oldDay.Month, oldDay.Day, 2, 0, 0, TimeSpan.Zero),
            40,
            80,
            -65,
            "[]");
        await database.Context.SaveChangesAsync();

        var maintenanceTask = CreateService(database.Context).RunAsync();
        await using var ingestionContext = new SaltMonitorDbContext(
            new DbContextOptionsBuilder<SaltMonitorDbContext>()
                .UseNpgsql(database.ConnectionString)
                .Options);
        ingestionContext.TelemetryReadings.Add(new()
        {
            DeviceId = subject.Device.Id,
            DeviceInstallationId = subject.Installation.Id,
            TankCalibrationRecordId = subject.Calibration.Id,
            BootId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            SequenceNumber = 1,
            ReceivedAtUtc = Now,
            UptimeMilliseconds = 1000,
            RawDistanceMm = 900,
            FillPercent = 70,
            Quality = 95,
            SampleCount = 8,
            WifiRssiDbm = -50,
            FirmwareVersion = "1.1.0",
            ErrorFlagsJson = "[]"
        });
        await ingestionContext.SaveChangesAsync();
        await maintenanceTask;

        Assert.True(await ingestionContext.TelemetryReadings.AnyAsync(
            reading => reading.BootId == Guid.Parse("33333333-3333-3333-3333-333333333333")));
    }

    private static TelemetryHistoryMaintenanceService CreateService(SaltMonitorDbContext context) =>
        new(
            context,
            new FixedTimeProvider(Now),
            Options.Create(new TelemetryHistoryOptions { DeleteBatchSize = 1 }),
            NullLogger<TelemetryHistoryMaintenanceService>.Instance);

    private static async Task<(Device Device, DeviceInstallation Installation, TankCalibrationRecord Calibration)>
        SeedSubjectAsync(SaltMonitorDbContext context)
    {
        var customer = new CustomerAccount
        {
            Id = Guid.NewGuid(), WaterFlexCustomerId = $"customer-{Guid.NewGuid():N}",
            DisplayName = "History customer", IsActive = true, LastSyncedAtUtc = Now
        };
        var location = new ServiceLocation
        {
            Id = Guid.NewGuid(), CustomerAccountId = customer.Id,
            WaterFlexLocationId = $"location-{Guid.NewGuid():N}", DisplayName = "History location",
            IsActive = true, LastSyncedAtUtc = Now
        };
        var tank = new Tank
        {
            Id = Guid.NewGuid(), ServiceLocationId = location.Id,
            WaterFlexAssetId = $"asset-{Guid.NewGuid():N}", Label = "History tank", IsActive = true
        };
        var device = new Device
        {
            Id = Guid.NewGuid(), SerialNumber = $"WF-{Guid.NewGuid():N}",
            Model = "Nano ESP32",
            Status = DeviceLifecycleStatus.Active, RegisteredAtUtc = Now, CommissionedAtUtc = Now
        };
        var installation = new DeviceInstallation
        {
            Id = Guid.NewGuid(), DeviceId = device.Id, TankId = tank.Id,
            InstalledAtUtc = Now, InstalledBy = "Test"
        };
        var calibration = new TankCalibrationRecord
        {
            Id = Guid.NewGuid(), DeviceInstallationId = installation.Id, Version = 1,
            TankDepthMm = 1500, CommissioningDistanceMm = 500,
            EffectiveFromUtc = Now, CreatedBy = "Test", CreatedAtUtc = Now
        };
        context.AddRange(customer, location, tank, device, installation, calibration);
        await context.SaveChangesAsync();
        return (device, installation, calibration);
    }

    private static void AddReading(
        SaltMonitorDbContext context,
        (Device Device, DeviceInstallation Installation, TankCalibrationRecord Calibration) subject,
        long sequence,
        DateTimeOffset receivedAt,
        double fillPercent,
        int quality,
        int rssi,
        string errors) =>
        context.TelemetryReadings.Add(new()
        {
            DeviceId = subject.Device.Id,
            DeviceInstallationId = subject.Installation.Id,
            TankCalibrationRecordId = subject.Calibration.Id,
            BootId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            SequenceNumber = sequence,
            ReceivedAtUtc = receivedAt,
            UptimeMilliseconds = sequence * 1000,
            RawDistanceMm = 1000 + (int)sequence,
            FillPercent = fillPercent,
            Quality = quality,
            SampleCount = 8,
            WifiRssiDbm = rssi,
            FirmwareVersion = sequence == 1 ? "1.0.0" : "1.1.0",
            ErrorFlagsJson = errors
        });

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private TestDatabase(SaltMonitorDbContext context, string connectionString)
        {
            Context = context;
            ConnectionString = connectionString;
        }
        public SaltMonitorDbContext Context { get; }
        public string ConnectionString { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connectionString = await TestPostgres.GetConnectionStringAsync(
                $"WaterFlexHistoryTests_{Guid.NewGuid():N}");
            var context = new SaltMonitorDbContext(
                new DbContextOptionsBuilder<SaltMonitorDbContext>().UseNpgsql(connectionString).Options);
            await context.Database.MigrateAsync();
            return new(context, connectionString);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.Database.EnsureDeletedAsync();
            await Context.DisposeAsync();
        }
    }
}
