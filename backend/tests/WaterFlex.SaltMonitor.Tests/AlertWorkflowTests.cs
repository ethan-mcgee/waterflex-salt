using Microsoft.EntityFrameworkCore;
using WaterFlex.SaltMonitor.Domain.Abstractions;
using WaterFlex.SaltMonitor.Domain.Model;
using WaterFlex.SaltMonitor.Domain.Monitoring;
using WaterFlex.SaltMonitor.Domain.Security;
using WaterFlex.SaltMonitor.Infrastructure;
using WaterFlex.SaltMonitor.Infrastructure.Persistence;
using WaterFlex.SaltMonitor.Ingestion;
using WaterFlex.SaltMonitor.Operations;
using Xunit;

namespace WaterFlex.SaltMonitor.Tests;

public sealed class AlertWorkflowTests
{
    [Fact]
    public async Task TrustedReadings_DebounceDeduplicateTransitionAndResolveOneAlert()
    {
        await using var database = await AlertDatabase.CreateAsync(SensorHealthStatus.Healthy);
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));
        var schedule = new MonitoringSchedule(TimeSpan.FromMinutes(1));
        var ingestion = new EfTelemetryIngestionService(
            database.Context,
            new TelemetryBatchValidator(clock),
            clock,
            schedule);
        var processor = new EfAlertWorkProcessor(database.Context, clock, schedule);
        var bootId = Guid.NewGuid();

        Assert.True((await ingestion.IngestAsync(database.DeviceId, Batch(bootId, 1, 800))).IsSuccess);
        Assert.True(await processor.ProcessNextAsync(CancellationToken.None));
        Assert.Empty(await database.Context.LowSaltAlerts.ToListAsync());

        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.True((await ingestion.IngestAsync(database.DeviceId, Batch(bootId, 2, 800))).IsSuccess);
        Assert.True(await processor.ProcessNextAsync(CancellationToken.None));
        var alert = Assert.Single(await database.Context.LowSaltAlerts.ToListAsync());
        Assert.Equal(LowSaltAlertStatus.Open, alert.Status);

        var ticket = await database.Context.DeliveryTickets.SingleAsync(item => item.LowSaltAlertId == alert.Id);
        Assert.Equal(DeliveryTicketStatus.Pending, ticket.Status);

        var ticketProcessor = new EfDeliveryTicketWorkProcessor(database.Context, clock, new StubDeliveryTicketGateway());
        Assert.True(await ticketProcessor.ProcessNextAsync(CancellationToken.None));
        database.Context.ChangeTracker.Clear();
        ticket = await database.Context.DeliveryTickets.SingleAsync(item => item.LowSaltAlertId == alert.Id);
        Assert.Equal(DeliveryTicketStatus.Created, ticket.Status);
        Assert.Equal($"STUB-{ticket.IdempotencyKey}", ticket.ExternalTicketId);

        var duplicate = await ingestion.IngestAsync(database.DeviceId, Batch(bootId, 2, 800));
        Assert.True(duplicate.IsSuccess);
        Assert.Equal(TelemetryReadingStatus.Duplicate, duplicate.Acknowledgement!.Readings[0].Status);
        Assert.False(await processor.ProcessNextAsync(CancellationToken.None));
        Assert.Single(await database.Context.LowSaltAlerts.ToListAsync());

        var operations = new EfAlertOperationsService(database.Context, clock);
        var actor = new StaffActor("operator", "Pilot Operator", StaffRole.WaterFlexEmployee, null, null);
        var acknowledged = await operations.TransitionAsync(
            alert.Id,
            AlertTransition.Acknowledge,
            new(alert.RowVersion.ToString()),
            actor,
            CancellationToken.None);
        Assert.True(acknowledged.IsSuccess);

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True((await ingestion.IngestAsync(database.DeviceId, Batch(bootId, 3, 500))).IsSuccess);
        Assert.True(await processor.ProcessNextAsync(CancellationToken.None));
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.True((await ingestion.IngestAsync(database.DeviceId, Batch(bootId, 4, 500))).IsSuccess);
        Assert.True(await processor.ProcessNextAsync(CancellationToken.None));

        database.Context.ChangeTracker.Clear();
        var resolved = await database.Context.LowSaltAlerts.SingleAsync();
        Assert.Equal(LowSaltAlertStatus.Resolved, resolved.Status);
        var events = await database.Context.LowSaltAlertAuditEvents
            .OrderBy(item => item.Id)
            .Select(item => item.EventType)
            .ToListAsync();
        Assert.Equal(["opened", "delivery_ticket_created", "acknowledge", "resolved"], events);

        var resolvedTicket = await database.Context.DeliveryTickets.SingleAsync(item => item.LowSaltAlertId == alert.Id);
        Assert.Equal(DeliveryTicketStatus.Resolved, resolvedTicket.Status);
        Assert.NotNull(resolvedTicket.ResolvedAtUtc);
    }

    [Fact]
    public async Task AlertResolvesBeforeGatewayRuns_TicketSkipsCreationAndIsResolvedDirectly()
    {
        await using var database = await AlertDatabase.CreateAsync(SensorHealthStatus.Healthy);
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));
        var schedule = new MonitoringSchedule(TimeSpan.FromMinutes(1));
        var ingestion = new EfTelemetryIngestionService(
            database.Context, new TelemetryBatchValidator(clock), clock, schedule);
        var processor = new EfAlertWorkProcessor(database.Context, clock, schedule);
        var bootId = Guid.NewGuid();

        Assert.True((await ingestion.IngestAsync(database.DeviceId, Batch(bootId, 1, 800))).IsSuccess);
        Assert.True(await processor.ProcessNextAsync(CancellationToken.None));
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.True((await ingestion.IngestAsync(database.DeviceId, Batch(bootId, 2, 800))).IsSuccess);
        Assert.True(await processor.ProcessNextAsync(CancellationToken.None));
        var alert = Assert.Single(await database.Context.LowSaltAlerts.ToListAsync());

        // Fill recovers fast (manual top-off) before the ticket outbox worker ever runs.
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True((await ingestion.IngestAsync(database.DeviceId, Batch(bootId, 3, 500))).IsSuccess);
        Assert.True(await processor.ProcessNextAsync(CancellationToken.None));
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.True((await ingestion.IngestAsync(database.DeviceId, Batch(bootId, 4, 500))).IsSuccess);
        Assert.True(await processor.ProcessNextAsync(CancellationToken.None));

        database.Context.ChangeTracker.Clear();
        Assert.Equal(LowSaltAlertStatus.Resolved, (await database.Context.LowSaltAlerts.SingleAsync()).Status);
        var ticket = await database.Context.DeliveryTickets.SingleAsync(item => item.LowSaltAlertId == alert.Id);
        Assert.Equal(DeliveryTicketStatus.Resolved, ticket.Status);
        Assert.Null(ticket.ExternalTicketId);

        // The resolve path already completed the outbox work item directly, so there's nothing
        // left to lease and the gateway is never called for a problem that's already fixed.
        var completedWorkItem = await database.Context.DeliveryTicketWorkItems
            .SingleAsync(item => item.DeliveryTicketId == ticket.Id);
        Assert.Equal(AlertWorkItemStatus.Completed, completedWorkItem.Status);

        var throwingGateway = new ThrowingDeliveryTicketGateway();
        var ticketProcessor = new EfDeliveryTicketWorkProcessor(database.Context, clock, throwingGateway);
        Assert.False(await ticketProcessor.ProcessNextAsync(CancellationToken.None));
        Assert.Equal(0, throwingGateway.CallCount);
    }

    [Fact]
    public async Task DeliveryTicketGatewayFailures_RetryThenDeadLetter()
    {
        await using var database = await AlertDatabase.CreateAsync(SensorHealthStatus.Healthy);
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));
        var schedule = new MonitoringSchedule(TimeSpan.FromMinutes(1));
        var ingestion = new EfTelemetryIngestionService(
            database.Context, new TelemetryBatchValidator(clock), clock, schedule);
        var processor = new EfAlertWorkProcessor(database.Context, clock, schedule);
        var bootId = Guid.NewGuid();

        Assert.True((await ingestion.IngestAsync(database.DeviceId, Batch(bootId, 1, 800))).IsSuccess);
        Assert.True(await processor.ProcessNextAsync(CancellationToken.None));
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.True((await ingestion.IngestAsync(database.DeviceId, Batch(bootId, 2, 800))).IsSuccess);
        Assert.True(await processor.ProcessNextAsync(CancellationToken.None));
        var alert = Assert.Single(await database.Context.LowSaltAlerts.ToListAsync());

        var throwingGateway = new ThrowingDeliveryTicketGateway();
        var ticketProcessor = new EfDeliveryTicketWorkProcessor(database.Context, clock, throwingGateway);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.True(await ticketProcessor.ProcessNextAsync(CancellationToken.None));
            clock.Advance(TimeSpan.FromMinutes(31));
        }
        Assert.Equal(5, throwingGateway.CallCount);

        database.Context.ChangeTracker.Clear();
        var ticket = await database.Context.DeliveryTickets.SingleAsync(item => item.LowSaltAlertId == alert.Id);
        Assert.Equal(DeliveryTicketStatus.Failed, ticket.Status);
        var workItem = await database.Context.DeliveryTicketWorkItems.SingleAsync(item => item.DeliveryTicketId == ticket.Id);
        Assert.Equal(AlertWorkItemStatus.DeadLetter, workItem.Status);
        Assert.Equal(5, workItem.AttemptCount);

        Assert.False(await ticketProcessor.ProcessNextAsync(CancellationToken.None));
    }

    private sealed class ThrowingDeliveryTicketGateway : IDeliveryTicketGateway
    {
        public int CallCount { get; private set; }

        public Task<DeliveryTicketResult> CreateDeliveryTicketAsync(
            DeliveryTicketRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException("Simulated gateway outage.");
        }
    }

    [Fact]
    public async Task DealerScope_RestrictsAlertVisibilityAndTransitions()
    {
        await using var database = await AlertDatabase.CreateAsync(SensorHealthStatus.Healthy);
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));
        var schedule = new MonitoringSchedule(TimeSpan.FromMinutes(1));
        var ingestion = new EfTelemetryIngestionService(
            database.Context, new TelemetryBatchValidator(clock), clock, schedule);
        var processor = new EfAlertWorkProcessor(database.Context, clock, schedule);
        var bootId = Guid.NewGuid();

        Assert.True((await ingestion.IngestAsync(database.DeviceId, Batch(bootId, 1, 800))).IsSuccess);
        Assert.True(await processor.ProcessNextAsync(CancellationToken.None));
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.True((await ingestion.IngestAsync(database.DeviceId, Batch(bootId, 2, 800))).IsSuccess);
        Assert.True(await processor.ProcessNextAsync(CancellationToken.None));
        var alert = Assert.Single(await database.Context.LowSaltAlerts.ToListAsync());

        var operations = new EfAlertOperationsService(database.Context, clock);
        var actor = new StaffActor("dealer-admin", "Dealer Admin", StaffRole.DealerAdministrator, "WF-D-OTHER", "Other Dealer");

        var ownScopeResults = await operations.SearchAsync(
            null, 1, 50, CancellationToken.None, scopeDealerExternalId: "WF-D-ALERT");
        var foreignScopeResults = await operations.SearchAsync(
            null, 1, 50, CancellationToken.None, scopeDealerExternalId: "WF-D-OTHER");
        var foreignGet = await operations.GetAsync(alert.Id, CancellationToken.None, scopeDealerExternalId: "WF-D-OTHER");
        var deniedTransition = await operations.TransitionAsync(
            alert.Id,
            AlertTransition.Acknowledge,
            new(alert.RowVersion.ToString()),
            actor,
            CancellationToken.None,
            scopeDealerExternalId: "WF-D-OTHER");

        Assert.Single(ownScopeResults.Items);
        Assert.Empty(foreignScopeResults.Items);
        Assert.Null(foreignGet);
        Assert.Equal(AlertTransitionFailure.NotFound, deniedTransition.Failure);
        Assert.Equal(LowSaltAlertStatus.Open, (await database.Context.LowSaltAlerts.SingleAsync()).Status);
    }

    [Fact]
    public async Task FaultedSensor_ReadingsNeverOpenAlert()
    {
        await using var database = await AlertDatabase.CreateAsync(SensorHealthStatus.Faulted);
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));
        var schedule = new MonitoringSchedule(TimeSpan.FromMinutes(1));
        var ingestion = new EfTelemetryIngestionService(database.Context, new TelemetryBatchValidator(clock), clock, schedule);
        var processor = new EfAlertWorkProcessor(database.Context, clock, schedule);
        var bootId = Guid.NewGuid();

        await ingestion.IngestAsync(database.DeviceId, Batch(bootId, 1, 800));
        await processor.ProcessNextAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(5));
        await ingestion.IngestAsync(database.DeviceId, Batch(bootId, 2, 800));
        await processor.ProcessNextAsync(CancellationToken.None);

        Assert.Empty(await database.Context.LowSaltAlerts.ToListAsync());
    }

    private static TelemetryBatch Batch(Guid bootId, long sequence, int distanceMm) => new(
        1,
        "pilot-test",
        [new(bootId, sequence, null, sequence * 60_000, distanceMm, 90, 1, -55, [])]);

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }

    private sealed class AlertDatabase : IAsyncDisposable
    {
        private AlertDatabase(SaltMonitorDbContext context, Guid deviceId)
        {
            Context = context;
            DeviceId = deviceId;
        }

        public SaltMonitorDbContext Context { get; }
        public Guid DeviceId { get; }

        public static async Task<AlertDatabase> CreateAsync(SensorHealthStatus sensorStatus)
        {
            var connectionString = await TestPostgres.GetConnectionStringAsync(
                $"WaterFlexAlertTests_{Guid.NewGuid():N}");
            var context = new SaltMonitorDbContext(
                new DbContextOptionsBuilder<SaltMonitorDbContext>().UseNpgsql(connectionString).Options);
            await context.Database.MigrateAsync();
            var now = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
            var dealer = new Dealer { Id = Guid.NewGuid(), ExternalId = "WF-D-ALERT", DisplayName = "Alert Dealer", IsActive = true };
            var customer = new CustomerAccount
            {
                Id = Guid.NewGuid(), WaterFlexCustomerId = "alert-customer", DisplayName = "Alert Customer",
                IsActive = true, LastSyncedAtUtc = now
            };
            var location = new ServiceLocation
            {
                Id = Guid.NewGuid(), CustomerAccountId = customer.Id, WaterFlexLocationId = "alert-location",
                DisplayName = "Alert Location", IsActive = true, LastSyncedAtUtc = now
            };
            var tank = new Tank { Id = Guid.NewGuid(), ServiceLocationId = location.Id, Label = "Softener", IsActive = true };
            var device = new Device
            {
                Id = Guid.NewGuid(), SerialNumber = $"WF-{Guid.NewGuid():N}", HardwareId = Guid.NewGuid().ToString("N")[..12],
                Model = "Nano ESP32", Status = DeviceLifecycleStatus.Active, RegisteredAtUtc = now,
                CommissionedAtUtc = now, LastSensorStatus = sensorStatus
            };
            var installation = new DeviceInstallation
            {
                Id = Guid.NewGuid(), DeviceId = device.Id, TankId = tank.Id, DealerId = dealer.Id, InstalledAtUtc = now
            };
            var calibration = new TankCalibrationRecord
            {
                Id = Guid.NewGuid(), DeviceInstallationId = installation.Id, Version = 1, TankDepthMm = 1000,
                CommissioningDistanceMm = 500, EffectiveFromUtc = now, CreatedBy = "test", CreatedAtUtc = now
            };
            context.AddRange(dealer, customer, location, tank, device, installation, calibration);
            await context.SaveChangesAsync();
            return new(context, device.Id);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.Database.EnsureDeletedAsync();
            await Context.DisposeAsync();
        }
    }
}
