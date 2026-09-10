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
    private static readonly StaffActor WaterFlexAdministrator = new(
        "wf-admin-avery", "Avery Patel", StaffRole.WaterFlexAdministrator, null, null);

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

        Assert.True(orders.IsSuccess);
        Assert.Equal(2, orders.WorkOrders.Count);
        Assert.Equal(["Newer Customer", "Older Customer"], orders.WorkOrders.Select(order => order.CustomerName));
    }

    [Fact]
    public async Task WaterFlexAdministrator_RequiresActiveScopeAndRetainsActorAuditIdentity()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedDealersAsync(database.Context);
        database.Context.Dealers.Add(new Dealer
        {
            Id = Guid.NewGuid(), ExternalId = "WF-D-INACTIVE", DisplayName = "Inactive Dealer", IsActive = false
        });
        await database.Context.SaveChangesAsync();
        var service = new EfInstallationWorkOrderService(database.Context, new FixedTimeProvider(Now));

        var missing = await service.CreateAsync(Request("Missing scope"), WaterFlexAdministrator);
        var inactive = await service.ListAsync(WaterFlexAdministrator, "WF-D-INACTIVE");
        var created = await service.CreateAsync(Request("Administrator-created"), WaterFlexAdministrator, Administrator.DealerExternalId);
        var listed = await service.ListAsync(WaterFlexAdministrator, Administrator.DealerExternalId);
        var cancelled = await service.CancelAsync(
            created.WorkOrder!.Id,
            new("Customer postponed", created.WorkOrder.RowVersion),
            WaterFlexAdministrator,
            Administrator.DealerExternalId);

        Assert.Equal(InstallationWorkOrderFailure.InvalidRequest, missing.Failure);
        Assert.Contains(missing.ValidationErrors, error => error.Field == "dealerExternalId");
        Assert.Equal(InstallationWorkOrderFailure.NotFound, inactive.Failure);
        Assert.True(listed.IsSuccess);
        Assert.Single(listed.WorkOrders);
        Assert.Equal(WaterFlexAdministrator.UserId, created.WorkOrder.CreatedByActorId);
        Assert.Equal(WaterFlexAdministrator.DisplayName, created.WorkOrder.CreatedBy);
        Assert.Equal(WaterFlexAdministrator.UserId, cancelled.WorkOrder!.CancelledByActorId);
        Assert.Equal(WaterFlexAdministrator.DisplayName, cancelled.WorkOrder.CancelledBy);
    }

    [Fact]
    public async Task WaterFlexAdministrator_CannotCancelOrderOutsideSelectedDealer()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedDealersAsync(database.Context);
        var service = new EfInstallationWorkOrderService(database.Context, new FixedTimeProvider(Now));
        var created = await service.CreateAsync(Request("Other dealer"), OtherAdministrator);

        var result = await service.CancelAsync(
            created.WorkOrder!.Id,
            new("Wrong dealer", created.WorkOrder.RowVersion),
            WaterFlexAdministrator,
            Administrator.DealerExternalId);

        Assert.Equal(InstallationWorkOrderFailure.NotFound, result.Failure);
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

    [Fact]
    public async Task Create_NormalizesStructuredFieldsAndLeavesTankUnset()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedDealersAsync(database.Context);
        var service = new EfInstallationWorkOrderService(database.Context, new FixedTimeProvider(Now));

        var result = await service.CreateAsync(
            new("  Ada ", " Lovelace  ", " 100 Main St ", " Madison ", " wi ", " 53703-1234 ", "  ", " Apt 2 "),
            Administrator);

        Assert.True(result.IsSuccess);
        Assert.Equal("Ada Lovelace", result.WorkOrder!.CustomerName);
        Assert.Equal("Ada", result.WorkOrder.FirstName);
        Assert.Equal("Lovelace", result.WorkOrder.LastName);
        Assert.Null(result.WorkOrder.LocationName);
        Assert.Equal("100 Main St, Apt 2, Madison, WI 53703-1234", result.WorkOrder.Address);
        Assert.Equal("WI", result.WorkOrder.State);
        Assert.Null(result.WorkOrder.TankLocation);
        Assert.Null((await database.Context.Tanks.SingleAsync()).Label);
    }

    [Theory]
    [InlineData("XX", "53703", "State")]
    [InlineData("WI", "5370", "ZipCode")]
    [InlineData("WI", "53703 1234", "ZipCode")]
    public async Task Create_RejectsInvalidStateAndZip(string state, string zipCode, string field)
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedDealersAsync(database.Context);
        var service = new EfInstallationWorkOrderService(database.Context, new FixedTimeProvider(Now));

        var result = await service.CreateAsync(
            new("Ada", "Lovelace", "100 Main St", "Madison", state, zipCode, null, null), Administrator);

        Assert.Equal(InstallationWorkOrderFailure.InvalidRequest, result.Failure);
        Assert.Contains(result.ValidationErrors, error => error.Field == field);
    }

    [Fact]
    public async Task Create_RejectsTrimmedValuesOverTheirMaximumLength()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedDealersAsync(database.Context);
        var service = new EfInstallationWorkOrderService(database.Context, new FixedTimeProvider(Now));

        var result = await service.CreateAsync(
            new($"  {new string('A', 101)}  ", "Lovelace", "100 Main St", "Madison", "WI", "53703", null, null),
            Administrator);

        Assert.Equal(InstallationWorkOrderFailure.InvalidRequest, result.Failure);
        Assert.Contains(result.ValidationErrors, error => error.Field == "FirstName" && error.Message.Contains("100"));
    }

    [Fact]
    public async Task List_ReturnsLegacySummariesWhenStructuredFieldsAreMissing()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedDealersAsync(database.Context);
        var dealer = await database.Context.Dealers.SingleAsync(candidate => candidate.ExternalId == Administrator.DealerExternalId);
        var customer = new CustomerAccount
        {
            Id = Guid.NewGuid(), WaterFlexCustomerId = "LEGACY-CUSTOMER", DisplayName = "Legacy Customer",
            IsActive = true, LastSyncedAtUtc = Now
        };
        var location = new ServiceLocation
        {
            Id = Guid.NewGuid(), CustomerAccountId = customer.Id, WaterFlexLocationId = "LEGACY-LOCATION",
            DisplayName = null, AddressSummary = "Old address summary", IsActive = true, LastSyncedAtUtc = Now,
            CustomerAccount = customer
        };
        var tank = new Tank
        {
            Id = Guid.NewGuid(), ServiceLocationId = location.Id, WaterFlexAssetId = "LEGACY-TANK",
            Label = null, IsActive = true, ServiceLocation = location
        };
        database.Context.InstallationWorkOrders.Add(new InstallationWorkOrderRecord
        {
            Id = Guid.NewGuid(), WorkOrderNumber = "WO-900001", DealerId = dealer.Id,
            CustomerAccountId = customer.Id, ServiceLocationId = location.Id, TankId = tank.Id,
            Status = WorkOrderStatus.Open, CreatedByActorId = "legacy", CreatedByDisplayName = "Legacy Import",
            CreatedAtUtc = Now, Dealer = dealer, CustomerAccount = customer, ServiceLocation = location, Tank = tank
        });
        await database.Context.SaveChangesAsync();

        var result = await new EfInstallationWorkOrderService(database.Context, new FixedTimeProvider(Now)).ListAsync(Administrator);

        var order = Assert.Single(result.WorkOrders);
        Assert.Equal("Legacy Customer", order.CustomerName);
        Assert.Equal("Old address summary", order.Address);
        Assert.Null(order.FirstName);
        Assert.Null(order.LocationName);
        Assert.Null(order.TankLocation);
    }

    private static CreateInstallationWorkOrderRequest Request(string customer) =>
        new(customer, "Customer", "100 Main St", "Madison", "WI", "53703", "Mechanical room", null);

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
