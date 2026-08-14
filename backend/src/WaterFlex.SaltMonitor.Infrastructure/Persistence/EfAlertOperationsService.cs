using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WaterFlex.SaltMonitor.Domain.Monitoring;
using WaterFlex.SaltMonitor.Domain.Security;
using WaterFlex.SaltMonitor.Operations;

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence;

public sealed class EfAlertOperationsService(
    SaltMonitorDbContext dbContext,
    TimeProvider timeProvider) : IAlertOperationsService
{
    public async Task<AlertPage> SearchAsync(
        LowSaltAlertStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = dbContext.LowSaltAlerts.AsNoTracking();
        if (status is not null) query = query.Where(alert => alert.Status == status);

        var total = await query.CountAsync(cancellationToken);
        var items = await Project(query)
            .OrderByDescending(alert => alert.OpenedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var deadLetters = await dbContext.AlertEvaluationWorkItems
            .CountAsync(item => item.Status == AlertWorkItemStatus.DeadLetter, cancellationToken);
        return new(items, total, page, pageSize, deadLetters);
    }

    public async Task<AlertDetail?> GetAsync(Guid alertId, CancellationToken cancellationToken)
    {
        var alert = await Project(dbContext.LowSaltAlerts.AsNoTracking().Where(item => item.Id == alertId))
            .SingleOrDefaultAsync(cancellationToken);
        if (alert is null) return null;

        var audit = await dbContext.LowSaltAlertAuditEvents
            .AsNoTracking()
            .Where(item => item.LowSaltAlertId == alertId)
            .OrderBy(item => item.OccurredAtUtc)
            .ThenBy(item => item.Id)
            .Select(item => new AlertAuditItem(
                item.Id,
                item.EventType,
                item.ActorType,
                item.ActorId,
                item.Reason,
                item.TelemetryReadingId,
                item.FillPercent,
                item.OccurredAtUtc))
            .ToListAsync(cancellationToken);
        return new(alert, audit);
    }

    public async Task<AlertTransitionResult> TransitionAsync(
        Guid alertId,
        AlertTransition transition,
        AlertTransitionRequest request,
        StaffActor actor,
        CancellationToken cancellationToken)
    {
        if (!uint.TryParse(request.ExpectedRowVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedVersion)
            || (transition == AlertTransition.Dismiss
                && (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)))
        {
            return new(null, AlertTransitionFailure.InvalidRequest);
        }

        var alert = await dbContext.LowSaltAlerts.SingleOrDefaultAsync(item => item.Id == alertId, cancellationToken);
        if (alert is null) return new(null, AlertTransitionFailure.NotFound);
        if (alert.RowVersion != expectedVersion) return new(null, AlertTransitionFailure.Conflict);

        var now = timeProvider.GetUtcNow();
        var valid = transition switch
        {
            AlertTransition.Acknowledge when alert.Status == LowSaltAlertStatus.Open => Acknowledge(alert, actor, now),
            AlertTransition.Approve when alert.Status is LowSaltAlertStatus.Open or LowSaltAlertStatus.Acknowledged => Approve(alert, actor, now),
            AlertTransition.Dismiss when alert.Status is LowSaltAlertStatus.Open or LowSaltAlertStatus.Acknowledged or LowSaltAlertStatus.Approved => Dismiss(alert, actor, request.Reason!, now),
            _ => false
        };
        if (!valid) return new(null, AlertTransitionFailure.InvalidState);

        dbContext.Entry(alert).Property(item => item.RowVersion).OriginalValue = expectedVersion;
        dbContext.LowSaltAlertAuditEvents.Add(new LowSaltAlertAuditEvent
        {
            LowSaltAlertId = alert.Id,
            EventType = transition.ToString().ToLowerInvariant(),
            ActorType = "staff",
            ActorId = actor.UserId,
            Reason = request.Reason?.Trim(),
            OccurredAtUtc = now
        });

        if (transition == AlertTransition.Dismiss)
        {
            var state = await dbContext.LowSaltAlertEvaluationStates
                .SingleOrDefaultAsync(item => item.DeviceInstallationId == alert.DeviceInstallationId, cancellationToken);
            if (state is null)
            {
                state = new LowSaltAlertEvaluationState { DeviceInstallationId = alert.DeviceInstallationId };
                dbContext.Add(state);
            }
            state.BelowEvidenceCount = 0;
            state.FirstBelowEvidenceAtUtc = null;
            state.RecoveryEvidenceCount = 0;
            state.FirstRecoveryEvidenceAtUtc = null;
            state.SuppressedUntilUtc = now.AddHours(24);
            state.UpdatedAtUtc = now;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(null, AlertTransitionFailure.Conflict);
        }

        return new(await GetAsync(alertId, cancellationToken), AlertTransitionFailure.None);
    }

    private static bool Acknowledge(LowSaltAlert alert, StaffActor actor, DateTimeOffset now)
    {
        alert.Status = LowSaltAlertStatus.Acknowledged;
        alert.AcknowledgedAtUtc = now;
        alert.AcknowledgedBy = actor.UserId;
        return true;
    }

    private static bool Approve(LowSaltAlert alert, StaffActor actor, DateTimeOffset now)
    {
        alert.Status = LowSaltAlertStatus.Approved;
        alert.ApprovedAtUtc = now;
        alert.ApprovedBy = actor.UserId;
        return true;
    }

    private static bool Dismiss(LowSaltAlert alert, StaffActor actor, string reason, DateTimeOffset now)
    {
        alert.Status = LowSaltAlertStatus.Dismissed;
        alert.DismissedAtUtc = now;
        alert.DismissedBy = actor.UserId;
        alert.DismissalReason = reason.Trim();
        return true;
    }

    private static IQueryable<AlertListItem> Project(IQueryable<LowSaltAlert> query) =>
        query.Select(alert => new AlertListItem(
            alert.Id,
            alert.DeviceInstallation.DeviceId,
            alert.DeviceInstallationId,
            alert.DeviceInstallation.Device.SerialNumber,
            alert.DeviceInstallation.Dealer != null ? alert.DeviceInstallation.Dealer.DisplayName : "Unassigned",
            alert.DeviceInstallation.Tank.ServiceLocation.CustomerAccount.DisplayName,
            alert.DeviceInstallation.Tank.ServiceLocation.DisplayName,
            alert.DeviceInstallation.Tank.Label,
            alert.Status,
            alert.OpenedAtUtc,
            alert.LastEvidenceAtUtc,
            alert.LastEvidenceFillPercent,
            alert.RowVersion.ToString()));
}
