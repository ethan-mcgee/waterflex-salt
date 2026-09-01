using WaterFlex.SaltMonitor.Domain.Security;
using WaterFlex.SaltMonitor.Domain.Monitoring;

namespace WaterFlex.SaltMonitor.Operations;

/// <summary>A low-salt alert as shown in the operations alert list, joined with device/customer context and its delivery ticket state.</summary>
public sealed record AlertListItem(
    Guid AlertId,
    Guid DeviceId,
    Guid InstallationId,
    string SerialNumber,
    string? DealerExternalId,
    string DealerName,
    string CustomerDisplayName,
    string LocationDisplayName,
    string TankLabel,
    LowSaltAlertStatus Status,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset LastEvidenceAtUtc,
    double LastEvidenceFillPercent,
    string RowVersion,
    DeliveryTicketStatus? TicketStatus,
    string? TicketExternalId);

/// <summary>One entry in an alert's audit trail: a state transition or system event, and who or what caused it.</summary>
public sealed record AlertAuditItem(
    long Id,
    string EventType,
    string ActorType,
    string ActorId,
    string? Reason,
    long? TelemetryReadingId,
    double? FillPercent,
    DateTimeOffset OccurredAtUtc);

/// <summary>State of the delivery ticket, if any, that was raised against the external delivery-ticket gateway for an alert.</summary>
public sealed record DeliveryTicketDetail(
    DeliveryTicketStatus Status,
    string? ExternalTicketId,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? ExternalCreatedAtUtc,
    DateTimeOffset? ResolvedAtUtc,
    string? LastError);

/// <summary>Full detail for a single alert, including its audit history and delivery ticket state.</summary>
public sealed record AlertDetail(
    AlertListItem Alert,
    IReadOnlyList<AlertAuditItem> AuditHistory,
    DeliveryTicketDetail? Ticket);

/// <summary>
/// One page of the alert list. <see cref="DeadLetterCount"/> surfaces alerts whose delivery-ticket
/// creation has repeatedly failed and needs staff attention, separate from the normal page count.
/// </summary>
public sealed record AlertPage(
    IReadOnlyList<AlertListItem> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int DeadLetterCount);

/// <summary>Staff-initiated transitions an alert can undergo. See Plan C for the full alert lifecycle.</summary>
public enum AlertTransition
{
    Acknowledge,
    Approve,
    Dismiss
}

/// <summary>Request to transition an alert. <see cref="ExpectedRowVersion"/> guards against acting on a stale view of the alert.</summary>
public sealed record AlertTransitionRequest(string ExpectedRowVersion, string? Reason = null);

/// <summary>Reasons an alert transition can be rejected.</summary>
public enum AlertTransitionFailure
{
    None,
    NotFound,
    InvalidState,
    InvalidRequest,
    Conflict
}

/// <summary>Outcome of attempting an alert transition.</summary>
public sealed record AlertTransitionResult(AlertDetail? Alert, AlertTransitionFailure Failure)
{
    public bool IsSuccess => Failure == AlertTransitionFailure.None;
}

/// <summary>Staff-facing read and transition operations over low-salt alerts.</summary>
public interface IAlertOperationsService
{
    Task<AlertPage> SearchAsync(
        LowSaltAlertStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken,
        string? scopeDealerExternalId = null);
    Task<AlertDetail?> GetAsync(
        Guid alertId,
        CancellationToken cancellationToken,
        string? scopeDealerExternalId = null);
    Task<AlertTransitionResult> TransitionAsync(
        Guid alertId,
        AlertTransition transition,
        AlertTransitionRequest request,
        StaffActor actor,
        CancellationToken cancellationToken,
        string? scopeDealerExternalId = null);
}

/// <summary>Drives the alert evaluation background work: opens, escalates, and auto-resolves alerts as telemetry comes in.</summary>
public interface IAlertWorkProcessor
{
    Task<bool> ProcessNextAsync(CancellationToken cancellationToken);
}

/// <summary>Drives the delivery-ticket outbox: creates tickets for newly opened alerts via the delivery-ticket gateway.</summary>
public interface IDeliveryTicketWorkProcessor
{
    Task<bool> ProcessNextAsync(CancellationToken cancellationToken);
}
