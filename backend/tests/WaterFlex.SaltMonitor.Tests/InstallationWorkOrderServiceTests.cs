using Microsoft.EntityFrameworkCore;
using WaterFlex.SaltMonitor.Domain.Security;
using WaterFlex.SaltMonitor.Infrastructure.Persistence;
using WaterFlex.SaltMonitor.Provisioning;
using Xunit;

namespace WaterFlex.SaltMonitor.Tests;

public sealed class InstallationWorkOrderServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 15, 0, 0, TimeSpan.Zero);
    private static readonly StaffActor Administrator = new(
        "north-star-admin", "Taylor Brooks", StaffRole.DealerAdministrator,
        "WF-D-NORTH-STAR", "North Star Water Systems");
    private static readonly StaffActor OtherAdministrator = new(
        "lakes-admin", "Sam Rivera", StaffRole.DealerAdministrator,
        "WF-D-LAKES-WATER", "Lakes Water Conditioning");

    [Fact]
    public async Task Create_GeneratesUniqueNumbersAndDedicatedTargets()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedDealersAsync(database.Context);
        var service = new EfInstallationWorkOrderService(database.Context, new FixedTimeProvider(Now));

        var first = await service.CreateAsync(Request("First customer"), Administrator);
        var second = await service.CreateAsync(Request("Second customer"), Administrator);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Matches("^WO-[0-9]{6}$", first.WorkOrder!.WorkOrderNumber);
        Assert.NotEqual(first.WorkOrder.WorkOrderNumber, second.WorkOrder!.WorkOrderNumber);
        Assert.Equal(2, await database.Context.CustomerAccounts.CountAsync());
        Assert.Equal(2, await database.Context.ServiceLocations.CountAsync());
        Assert.Equal(2, await database.Context.Tanks.CountAsync());
        Assert.All(await database.Context.CustomerAccounts.ToArrayAsync(), customer => Assert.StartsWith("WO-C-", customer.WaterFlexCustomerId));
    }

    [Fact]
    public async Task List_IsDealerScopedAndNewestFirst()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedDealersAsync(database.Context);
        var clock = new MutableTimeProvider(Now);
        var service = new EfInstallationWorkOrderService(database.Context, clock);
        await service.CreateAsync(Request("Older"), Administrator);
        clock.Advance(TimeSpan.FromMinutes(1));
        await service.CreateAsync(Request("Other dealer"), OtherAdministrator);
        clock.Advance(TimeSpan.FromMinutes(1));
        await service.CreateAsync(Request("Newer"), Administrator);

        var orders = await service.ListAsync(Administrator);

        Assert.Equal(2, orders.Count);
        Assert.Equal(["Newer", "Older"], orders.Select(order => order.CustomerName));
    }

    [Fact]
    public async Task Cancel_RequiresReasonAndCurrentRowVersionAndRetainsTargets()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedDealersAsync(database.Context);
        var service = new EfInstallationWorkOrderService(database.Context, new FixedTimeProvider(Now));
        var created = await service.CreateAsync(Request("Customer"), Administrator);

        var missingReason = await service.CancelAsync(created.WorkOrder!.Id, new(" ", created.WorkOrder.RowVersion), Administrator);
        var stale = await service.CancelAsync(created.WorkOrder.Id, new("Customer cancelled", created.WorkOrder.RowVersion + 1), Administrator);
        var cancelled = await service.CancelAsync(created.WorkOrder.Id, new("Customer cancelled", created.WorkOrder.RowVersion), Administrator);

        Assert.Equal(InstallationWorkOrderFailure.InvalidRequest, missingReason.Failure);
        Assert.Equal(InstallationWorkOrderFailure.Conflict, stale.Failure);
        Assert.True(cancelled.IsSuccess);
        Assert.Equal(WorkOrderStatus.Cancelled, cancelled.WorkOrder!.Status);
        Assert.Equal("Taylor Brooks", cancelled.WorkOrder.CancelledBy);
        Assert.Equal(1, await database.Context.CustomerAccounts.CountAsync());
        Assert.Equal(1, await database.Context.ServiceLocations.CountAsync());
        Assert.Equal(1, await database.Context.Tanks.CountAsync());
    }

    [Fact]
    public async Task Cancel_RejectsOrderWithLiveCommissioningSession()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedDealersAsync(database.Context);
        var service = new EfInstallationWorkOrderService(database.Context, new FixedTimeProvider(Now));
        var created = await service.CreateAsync(Request("Customer"), Administrator);
        var order = await database.Context.InstallationWorkOrders.SingleAsync(candidate => candidate.Id == created.WorkOrder!.Id);
        var device = new Device
        {
            Id = Guid.NewGuid(), SerialNumber = "WF-NANO-WO-TEST", Model = "Arduino Nano ESP32",
            Status = DeviceLifecycleStatus.Commissioning, RegisteredAtUtc = Now
        };
        database.Context.CommissioningSessions.Add(new CommissioningSession
        {
            Id = Guid.NewGuid(), DeviceId = device.Id, DealerId = order.DealerId, TankId = order.TankId,
            InstallationWorkOrderId = order.Id, Status = CommissioningSessionStatus.PendingSensor,
            TankDepthMm = 1500, CreatedByActorId = "technician", CreatedByDisplayName = "Technician",
            CreatedAtUtc = Now, ExpiresAtUtc = Now.AddMinutes(30), Device = device
        });
        await database.Context.SaveChangesAsync();

        var cancelled = await service.CancelAsync(order.Id, new("Customer cancelled", created.WorkOrder!.RowVersion), Administrator);

        Assert.Equal(InstallationWorkOrderFailure.Conflict, cancelled.Failure);
        Assert.Equal(WorkOrderStatus.Open, (await database.Context.InstallationWorkOrders.SingleAsync()).Status);
    }

    private static CreateInstallationWorkOrderRequest Request(string customer) =>
        new(customer, "Mechanical room", "100 Main St, Madison, WI", "Primary softener");

    private static async Task SeedDealersAsync(SaltMonitorDbContext context)
    {
        context.Dealers.AddRange(
            new Dealer { Id = Guid.NewGuid(), ExternalId = Administrator.DealerExternalId!, DisplayName = Administrator.DealerName!, IsActive = true },
            new Dealer { Id = Guid.NewGuid(), ExternalId = OtherAdministrator.DealerExternalId!, DisplayName = OtherAdministrator.DealerName!, IsActive = true });
        await context.SaveChangesAsync();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now = now.Add(duration);
    }

    private sealed class TestDatabase(SaltMonitorDbContext context) : IAsyncDisposable
    {
        public SaltMonitorDbContext Context { get; } = context;
        public static async Task<TestDatabase> CreateAsync()
        {
            var connectionString = await TestPostgres.GetConnectionStringAsync($"WaterFlexWorkOrderTests_{Guid.NewGuid():N}");
            var options = new DbContextOptionsBuilder<SaltMonitorDbContext>().UseNpgsql(connectionString).Options;
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
