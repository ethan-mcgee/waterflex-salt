using WaterFlex.SaltMonitor.Domain.Security;
using WaterFlex.SaltMonitor.Domain.Monitoring;

namespace WaterFlex.SaltMonitor.Operations;

public sealed record AlertListItem(
    Guid AlertId,
    Guid DeviceId,
    Guid InstallationId,
    string SerialNumber,
    string DealerName,
    string CustomerDisplayName,
    string LocationDisplayName,
    string TankLabel,
    LowSaltAlertStatus Status,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset LastEvidenceAtUtc,
    double LastEvidenceFillPercent,
    string RowVersion);

public sealed record AlertAuditItem(
    long Id,
    string EventType,
    string ActorType,
    string ActorId,
    string? Reason,
    long? TelemetryReadingId,
    double? FillPercent,
    DateTimeOffset OccurredAtUtc);

public sealed record AlertDetail(AlertListItem Alert, IReadOnlyList<AlertAuditItem> AuditHistory);

public sealed record AlertPage(
    IReadOnlyList<AlertListItem> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int DeadLetterCount);

public enum AlertTransition
{
    Acknowledge,
    Approve,
    Dismiss
}

public sealed record AlertTransitionRequest(string ExpectedRowVersion, string? Reason = null);

public enum AlertTransitionFailure
{
    None,
    NotFound,
    InvalidState,
    InvalidRequest,
    Conflict
}

public sealed record AlertTransitionResult(AlertDetail? Alert, AlertTransitionFailure Failure)
{
    public bool IsSuccess => Failure == AlertTransitionFailure.None;
}

public interface IAlertOperationsService
{
    Task<AlertPage> SearchAsync(LowSaltAlertStatus? status, int page, int pageSize, CancellationToken cancellationToken);
    Task<AlertDetail?> GetAsync(Guid alertId, CancellationToken cancellationToken);
    Task<AlertTransitionResult> TransitionAsync(
        Guid alertId,
        AlertTransition transition,
        AlertTransitionRequest request,
        StaffActor actor,
        CancellationToken cancellationToken);
}

public interface IAlertWorkProcessor
{
    Task<bool> ProcessNextAsync(CancellationToken cancellationToken);
}
