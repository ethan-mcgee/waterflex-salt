using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WaterFlex.SaltMonitor.Domain.Security;
using WaterFlex.SaltMonitor.Infrastructure.Persistence;
using WaterFlex.SaltMonitor.Provisioning;
using Xunit;

namespace WaterFlex.SaltMonitor.Tests;

public sealed class BootstrapProvisioningServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 24, 16, 0, 0, TimeSpan.Zero);
    private static readonly StaffActor NorthStarTechnician = new(
        "north-star-jordan",
        "Jordan Lee",
        StaffRole.DealerTechnician,
        "WF-D-NORTH-STAR",
        "North Star Water Systems");
    private static readonly StaffActor LakesTechnician = new(
        "lakes-water-sam",
        "Sam Rivera",
        StaffRole.DealerTechnician,
        "WF-D-LAKES-WATER",
        "Lakes Water Conditioning");

    [Fact]
    public async Task FactoryRegistration_StoresRegisteredInventoryAndHashOnly()
    {
        await using var database = await TestDatabase.CreateAsync();
        var timeProvider = new MutableTimeProvider(Now);
        var service = new EfFactoryDeviceRegistrationService(database.Context, timeProvider);
        var secretHash = SHA256.HashData(Encoding.UTF8.GetBytes("factory-secret"));

        var result = await service.RegisterAsync(
            CreateFactoryRequest(secretHash),
            "factory-operator-01");

        Assert.True(result.IsSuccess);
        var device = await database.Context.Devices.SingleAsync();
        var credential = await database.Context.DeviceBootstrapCredentials.SingleAsync();
        var auditEvent = await database.Context.ProvisioningAuditEvents.SingleAsync();
        Assert.Equal(DeviceLifecycleStatus.Registered, device.Status);
        Assert.Null(device.CommissionedAtUtc);
        Assert.Equal(secretHash, credential.SecretHash);
        Assert.DoesNotContain("factory-secret", auditEvent.DetailsJson, StringComparison.Ordinal);
        Assert.Equal("factory_device_registered", auditEvent.EventType);
    }

    [Fact]
    public async Task SessionCreation_ReservesRegisteredDeviceWithoutInstallationOrOperationalToken()
    {
        await using var database = await TestDatabase.CreateAsync();
        var timeProvider = new MutableTimeProvider(Now);
        await RegisterFactoryDeviceAsync(database.Context, timeProvider);
        var service = CreateSessionService(database.Context, timeProvider);

        var result = await service.CreateAsync(CreateSessionRequest(), NorthStarTechnician);

        Assert.True(result.IsSuccess);
        Assert.Equal(CommissioningSessionStatus.PendingSensor, result.Session!.Status);
        Assert.Equal(Now.AddMinutes(30), result.Session.ExpiresAtUtc);
        Assert.Equal(0, await database.Context.DeviceInstallations.CountAsync());
        Assert.Equal(0, await database.Context.DeviceCredentials.CountAsync());
        Assert.Equal(
            DeviceLifecycleStatus.Commissioning,
            (await database.Context.Devices.SingleAsync()).Status);
        Assert.Equal(1, await database.Context.CommissioningSessions.CountAsync());
    }

    [Fact]
    public async Task SessionLookup_IsDealerScopedAndCancellationReleasesDevice()
    {
        await using var database = await TestDatabase.CreateAsync();
        var timeProvider = new MutableTimeProvider(Now);
        await RegisterFactoryDeviceAsync(database.Context, timeProvider);
        var service = CreateSessionService(database.Context, timeProvider);
        var created = await service.CreateAsync(CreateSessionRequest(), NorthStarTechnician);

        var otherDealer = await service.GetAsync(created.Session!.SessionId, LakesTechnician);
        var cancelled = await service.CancelAsync(created.Session.SessionId, NorthStarTechnician);

        Assert.Equal(CommissioningSessionFailure.SessionNotFound, otherDealer.Failure);
        Assert.True(cancelled.IsSuccess);
        Assert.Equal(CommissioningSessionStatus.Cancelled, cancelled.Session!.Status);
        Assert.Equal(
            DeviceLifecycleStatus.Registered,
            (await database.Context.Devices.SingleAsync()).Status);
    }

    [Fact]
    public async Task SessionLookup_ExpiresPendingReservationAndReleasesDevice()
    {
        await using var database = await TestDatabase.CreateAsync();
        var timeProvider = new MutableTimeProvider(Now);
        await RegisterFactoryDeviceAsync(database.Context, timeProvider);
        var service = CreateSessionService(database.Context, timeProvider);
        var created = await service.CreateAsync(CreateSessionRequest(), NorthStarTechnician);
        timeProvider.Advance(TimeSpan.FromMinutes(31));

        var expired = await service.GetAsync(created.Session!.SessionId, NorthStarTechnician);

        Assert.True(expired.IsSuccess);
        Assert.Equal(CommissioningSessionStatus.Expired, expired.Session!.Status);
        Assert.Equal(
            DeviceLifecycleStatus.Registered,
            (await database.Context.Devices.SingleAsync()).Status);
    }

    [Fact]
    public async Task WorkOrderSession_RequiresTankLocationWhenOrderDoesNotProvideOne()
    {
        await using var database = await TestDatabase.CreateAsync();
        var timeProvider = new MutableTimeProvider(Now);
        await RegisterFactoryDeviceAsync(database.Context, timeProvider);
        var service = CreateSessionService(database.Context, timeProvider);

        var result = await service.CreateFromWorkOrderAsync(
            new("WO-82418", "WF-BOOT-0001", null, 150m),
            NorthStarTechnician);

        Assert.Equal(CommissioningSessionFailure.TankLocationRequired, result.Failure);
        Assert.Contains(result.ValidationErrors, error => error.Field == nameof(CreateWorkOrderCommissioningSessionRequest.TankLocation));
    }

    [Fact]
    public async Task WorkOrderLookup_IsDealerScoped()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateSessionService(database.Context, new MutableTimeProvider(Now));

        var visible = await service.FindWorkOrderAsync("WO-82417", NorthStarTechnician);
        var hidden = await service.FindWorkOrderAsync("WO-82417", LakesTechnician);

        Assert.NotNull(visible);
        Assert.Equal("North Ridge Apartments", visible!.CustomerDisplayName);
        Assert.Null(hidden);
    }

    private static EfCommissioningSessionService CreateSessionService(
        SaltMonitorDbContext context,
        TimeProvider timeProvider) =>
        new(
            context,
            new DevelopmentWaterFlexCustomerDirectory(),
            new DevelopmentInstallationWorkOrderDirectory(),
            timeProvider);

    private static async Task RegisterFactoryDeviceAsync(
        SaltMonitorDbContext context,
        TimeProvider timeProvider)
    {
        var service = new EfFactoryDeviceRegistrationService(context, timeProvider);
        var result = await service.RegisterAsync(
            CreateFactoryRequest(SHA256.HashData(Encoding.UTF8.GetBytes("factory-secret"))),
            "factory-operator-01");
        Assert.True(result.IsSuccess);
    }

    private static RegisterFactoryDeviceRequest CreateFactoryRequest(byte[] secretHash) =>
        new(
            "WF-BOOT-0001",
            "A1:B2:C3:D4:E5:F6",
            "Arduino Nano ESP32",
            "wf_boot_test_0001",
            Convert.ToBase64String(secretHash),
            "1.0.0",
            "pilot-v1");

    private static CreateCommissioningSessionRequest CreateSessionRequest() =>
        new(
            "WF-C-10482",
            "WF-L-10482-01",
            "WF-A-10482-S1",
            "WF-BOOT-0001",
            "WO-BOOT-1001",
            150m);

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan duration) => now = now.Add(duration);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private TestDatabase(SaltMonitorDbContext context) => Context = context;

        public SaltMonitorDbContext Context { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var databaseName = $"WaterFlexBootstrapServiceTests_{Guid.NewGuid():N}";
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
