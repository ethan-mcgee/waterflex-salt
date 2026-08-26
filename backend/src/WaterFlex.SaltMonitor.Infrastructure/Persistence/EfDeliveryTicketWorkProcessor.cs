using Microsoft.EntityFrameworkCore;
using WaterFlex.SaltMonitor.Domain.Abstractions;
using WaterFlex.SaltMonitor.Domain.Model;
using WaterFlex.SaltMonitor.Domain.Monitoring;
using WaterFlex.SaltMonitor.Operations;

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence;

/// <summary>
/// Drains the delivery-ticket outbox: for each pending <see cref="DeliveryTicketWorkItem"/>,
/// calls <see cref="IDeliveryTicketGateway"/> to open the ticket in WaterFlex/RouteFlex. The
/// gateway call happens outside any database transaction since it's an external HTTP request;
/// idempotency is enforced via the ticket's unique <see cref="DeliveryTicket.IdempotencyKey"/>.
/// </summary>
public sealed class EfDeliveryTicketWorkProcessor(
    SaltMonitorDbContext dbContext,
    TimeProvider timeProvider,
    IDeliveryTicketGateway gateway) : IDeliveryTicketWorkProcessor
{
    private const int MaximumAttempts = 5;
    private const string TenantId = "waterflex";
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(1);

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var leaseId = Guid.NewGuid();
        var workItemId = await TryLeaseAsync(leaseId, cancellationToken);
        if (workItemId is null) return false;

        try
        {
            await ProcessAsync(workItemId.Value, leaseId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            dbContext.ChangeTracker.Clear();
            await RecordFailureAsync(workItemId.Value, leaseId, exception, cancellationToken);
        }
        return true;
    }

    private async Task<long?> TryLeaseAsync(Guid leaseId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var candidateId = await dbContext.DeliveryTicketWorkItems
                .AsNoTracking()
                .Where(item =>
                    (item.Status == AlertWorkItemStatus.Pending && item.AvailableAtUtc <= now)
                    || (item.Status == AlertWorkItemStatus.Processing && item.LeasedUntilUtc < now))
                .OrderBy(item => item.Id)
                .Select(item => (long?)item.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (candidateId is null) return null;

            var affected = await dbContext.DeliveryTicketWorkItems
                .Where(item => item.Id == candidateId
                    && ((item.Status == AlertWorkItemStatus.Pending && item.AvailableAtUtc <= now)
                        || (item.Status == AlertWorkItemStatus.Processing && item.LeasedUntilUtc < now)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Status, AlertWorkItemStatus.Processing)
                    .SetProperty(item => item.LeaseId, leaseId)
                    .SetProperty(item => item.LeasedUntilUtc, now.Add(LeaseDuration)), cancellationToken);
            if (affected == 1) return candidateId;
        }
        return null;
    }

    private async Task ProcessAsync(long workItemId, Guid leaseId, CancellationToken cancellationToken)
    {
        var workItem = await dbContext.DeliveryTicketWorkItems
            .SingleAsync(item => item.Id == workItemId && item.LeaseId == leaseId, cancellationToken);
        var ticket = await dbContext.DeliveryTickets
            .Include(item => item.LowSaltAlert)
                .ThenInclude(alert => alert.DeviceInstallation)
                    .ThenInclude(installation => installation.Device)
            .Include(item => item.LowSaltAlert)
                .ThenInclude(alert => alert.DeviceInstallation)
                    .ThenInclude(installation => installation.Tank)
                        .ThenInclude(tank => tank.ServiceLocation)
                            .ThenInclude(location => location.CustomerAccount)
            .SingleAsync(item => item.Id == workItem.DeliveryTicketId, cancellationToken);

        // The alert may have already resolved (fast manual top-off) by the time this work item
        // is leased; the recovery path already completed it, but guard against the race anyway.
        if (ticket.Status is DeliveryTicketStatus.Resolved or DeliveryTicketStatus.Created)
        {
            Complete(workItem, timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var alert = ticket.LowSaltAlert;
        var installation = alert.DeviceInstallation;
        var request = new DeliveryTicketRequest(
            TenantId: TenantId,
            WaterFlexCustomerRef: installation.Tank.ServiceLocation.CustomerAccount.WaterFlexCustomerId,
            DeviceId: installation.Device.SerialNumber,
            FillPercent: alert.LastEvidenceFillPercent,
            ThresholdPercent: MonitoringPolicy.LowFillThresholdPercent,
            ReadingTimestamp: alert.LastEvidenceAtUtc,
            IdempotencyKey: ticket.IdempotencyKey);

        var result = await gateway.CreateDeliveryTicketAsync(request, cancellationToken);

        var now = timeProvider.GetUtcNow();
        ticket.Status = DeliveryTicketStatus.Created;
        ticket.ExternalTicketId = result.ExternalTicketId;
        ticket.ExternalCreatedAtUtc = now;
        dbContext.Add(new LowSaltAlertAuditEvent
        {
            LowSaltAlertId = alert.Id,
            EventType = "delivery_ticket_created",
            ActorType = "system",
            ActorId = "delivery-ticket-gateway",
            Reason = result.ExternalTicketId,
            OccurredAtUtc = now
        });

        Complete(workItem, now);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task RecordFailureAsync(
        long workItemId,
        Guid leaseId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var workItem = await dbContext.DeliveryTicketWorkItems
            .SingleOrDefaultAsync(item => item.Id == workItemId && item.LeaseId == leaseId, cancellationToken);
        if (workItem is null) return;

        workItem.AttemptCount++;
        workItem.LastError = exception.Message.Length <= 1000
            ? exception.Message
            : exception.Message[..1000];
        workItem.LeaseId = null;
        workItem.LeasedUntilUtc = null;
        if (workItem.AttemptCount >= MaximumAttempts)
        {
            workItem.Status = AlertWorkItemStatus.DeadLetter;
            var ticket = await dbContext.DeliveryTickets
                .SingleOrDefaultAsync(item => item.Id == workItem.DeliveryTicketId, cancellationToken);
            if (ticket is not null && ticket.Status == DeliveryTicketStatus.Pending)
            {
                ticket.Status = DeliveryTicketStatus.Failed;
                ticket.LastError = workItem.LastError;
            }
        }
        else
        {
            workItem.Status = AlertWorkItemStatus.Pending;
            var delayMinutes = Math.Min(30, 1 << workItem.AttemptCount);
            workItem.AvailableAtUtc = timeProvider.GetUtcNow().AddMinutes(delayMinutes);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void Complete(DeliveryTicketWorkItem workItem, DateTimeOffset now)
    {
        workItem.Status = AlertWorkItemStatus.Completed;
        workItem.CompletedAtUtc = now;
        workItem.LeaseId = null;
        workItem.LeasedUntilUtc = null;
        workItem.LastError = null;
    }
}
