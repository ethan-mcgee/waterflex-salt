using System.Data;
using Microsoft.EntityFrameworkCore;
using WaterFlex.SaltMonitor.Domain.Monitoring;
using WaterFlex.SaltMonitor.Ingestion;
using WaterFlex.SaltMonitor.Operations;

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence;

public sealed class EfAlertWorkProcessor(
    SaltMonitorDbContext dbContext,
    TimeProvider timeProvider,
    MonitoringSchedule monitoringSchedule) : IAlertWorkProcessor
{
    private const int MaximumAttempts = 5;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan EvidenceSpacing = TimeSpan.FromMinutes(5);

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var leaseId = Guid.NewGuid();
        var workItemId = await TryLeaseAsync(leaseId, cancellationToken);
        if (workItemId is null) return false;

        try
        {
            await EvaluateAsync(workItemId.Value, leaseId, cancellationToken);
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
            var candidateId = await dbContext.AlertEvaluationWorkItems
                .AsNoTracking()
                .Where(item =>
                    (item.Status == AlertWorkItemStatus.Pending && item.AvailableAtUtc <= now)
                    || (item.Status == AlertWorkItemStatus.Processing && item.LeasedUntilUtc < now))
                .OrderBy(item => item.Id)
                .Select(item => (long?)item.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (candidateId is null) return null;

            var affected = await dbContext.AlertEvaluationWorkItems
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

    private async Task EvaluateAsync(long workItemId, Guid leaseId, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var workItem = await dbContext.AlertEvaluationWorkItems
            .Include(item => item.TelemetryReading)
            .ThenInclude(reading => reading.Device)
            .SingleAsync(item => item.Id == workItemId && item.LeaseId == leaseId, cancellationToken);
        var reading = workItem.TelemetryReading;
        var now = timeProvider.GetUtcNow();

        var state = await dbContext.LowSaltAlertEvaluationStates
            .SingleOrDefaultAsync(item => item.DeviceInstallationId == reading.DeviceInstallationId, cancellationToken);
        if (state is null)
        {
            state = new LowSaltAlertEvaluationState
            {
                DeviceInstallationId = reading.DeviceInstallationId,
                UpdatedAtUtc = now
            };
            dbContext.Add(state);
        }

        if (state.LastProcessedTelemetryReadingId is { } previousId && previousId >= reading.Id)
        {
            Complete(workItem, now);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        state.LastProcessedTelemetryReadingId = reading.Id;
        state.UpdatedAtUtc = now;
        var trusted = reading.Quality >= TelemetryBatchValidator.MinimumOperationalQuality
            && reading.ErrorFlagsJson == "[]"
            && reading.Device.LastSensorStatus == SensorHealthStatus.Healthy
            && now - reading.ReceivedAtUtc < monitoringSchedule.StaleAfter;
        if (!trusted)
        {
            ResetEvidence(state);
            Complete(workItem, now);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var activeAlert = await dbContext.LowSaltAlerts
            .SingleOrDefaultAsync(alert => alert.DeviceInstallationId == reading.DeviceInstallationId
                && (alert.Status == LowSaltAlertStatus.Open
                    || alert.Status == LowSaltAlertStatus.Acknowledged
                    || alert.Status == LowSaltAlertStatus.Approved), cancellationToken);

        if (activeAlert is null)
        {
            EvaluateOpening(state, reading, now);
        }
        else
        {
            EvaluateRecovery(state, activeAlert, reading, now);
        }

        Complete(workItem, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private void EvaluateOpening(
        LowSaltAlertEvaluationState state,
        TelemetryReadingRecord reading,
        DateTimeOffset now)
    {
        if (state.SuppressedUntilUtc is { } suppressedUntil && suppressedUntil > now)
        {
            ResetEvidence(state);
            return;
        }
        state.SuppressedUntilUtc = null;

        if (reading.FillPercent >= MonitoringPolicy.LowFillThresholdPercent)
        {
            state.BelowEvidenceCount = 0;
            state.FirstBelowEvidenceAtUtc = null;
            return;
        }

        if (state.FirstBelowEvidenceAtUtc is null)
        {
            state.FirstBelowEvidenceAtUtc = reading.ReceivedAtUtc;
            state.BelowEvidenceCount = 1;
            return;
        }

        if (reading.ReceivedAtUtc - state.FirstBelowEvidenceAtUtc < EvidenceSpacing) return;

        var alert = new LowSaltAlert
        {
            Id = Guid.NewGuid(),
            DeviceInstallationId = reading.DeviceInstallationId,
            Status = LowSaltAlertStatus.Open,
            OpenedAtUtc = now,
            LastEvidenceAtUtc = reading.ReceivedAtUtc,
            LastEvidenceFillPercent = reading.FillPercent
        };
        dbContext.Add(alert);
        dbContext.Add(new LowSaltAlertAuditEvent
        {
            LowSaltAlertId = alert.Id,
            EventType = "opened",
            ActorType = "system",
            ActorId = "low-salt-evaluator",
            TelemetryReadingId = reading.Id,
            FillPercent = reading.FillPercent,
            OccurredAtUtc = now
        });
        state.BelowEvidenceCount = 0;
        state.FirstBelowEvidenceAtUtc = null;
    }

    private void EvaluateRecovery(
        LowSaltAlertEvaluationState state,
        LowSaltAlert alert,
        TelemetryReadingRecord reading,
        DateTimeOffset now)
    {
        alert.LastEvidenceAtUtc = reading.ReceivedAtUtc;
        alert.LastEvidenceFillPercent = reading.FillPercent;
        if (reading.FillPercent < 40d)
        {
            state.RecoveryEvidenceCount = 0;
            state.FirstRecoveryEvidenceAtUtc = null;
            return;
        }

        if (state.FirstRecoveryEvidenceAtUtc is null)
        {
            state.FirstRecoveryEvidenceAtUtc = reading.ReceivedAtUtc;
            state.RecoveryEvidenceCount = 1;
            return;
        }
        if (reading.ReceivedAtUtc - state.FirstRecoveryEvidenceAtUtc < EvidenceSpacing) return;

        alert.Status = LowSaltAlertStatus.Resolved;
        alert.ResolvedAtUtc = now;
        dbContext.Add(new LowSaltAlertAuditEvent
        {
            LowSaltAlertId = alert.Id,
            EventType = "resolved",
            ActorType = "system",
            ActorId = "low-salt-evaluator",
            TelemetryReadingId = reading.Id,
            FillPercent = reading.FillPercent,
            OccurredAtUtc = now
        });
        ResetEvidence(state);
    }

    private async Task RecordFailureAsync(
        long workItemId,
        Guid leaseId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var workItem = await dbContext.AlertEvaluationWorkItems
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
        }
        else
        {
            workItem.Status = AlertWorkItemStatus.Pending;
            var delayMinutes = Math.Min(30, 1 << workItem.AttemptCount);
            workItem.AvailableAtUtc = timeProvider.GetUtcNow().AddMinutes(delayMinutes);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void Complete(AlertEvaluationWorkItem workItem, DateTimeOffset now)
    {
        workItem.Status = AlertWorkItemStatus.Completed;
        workItem.CompletedAtUtc = now;
        workItem.LeaseId = null;
        workItem.LeasedUntilUtc = null;
        workItem.LastError = null;
    }

    private static void ResetEvidence(LowSaltAlertEvaluationState state)
    {
        state.BelowEvidenceCount = 0;
        state.FirstBelowEvidenceAtUtc = null;
        state.RecoveryEvidenceCount = 0;
        state.FirstRecoveryEvidenceAtUtc = null;
    }
}
