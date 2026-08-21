using WaterFlex.SaltMonitor.Domain.Security;

namespace WaterFlex.SaltMonitor.Operations;

public sealed record StaffMemberSummary(
    Guid Id,
    string Email,
    string DisplayName,
    StaffRole Role,
    string? DealerExternalId,
    string? DealerName,
    StaffIdentityState State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    uint RowVersion);

public sealed record StaffInvitationSummary(
    Guid Id,
    string Email,
    string DisplayName,
    StaffRole Role,
    string? DealerExternalId,
    string? DealerName,
    StaffInvitationStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string? FailureReason);

public sealed record CreateStaffInvitationRequest(
    string Email,
    string DisplayName,
    StaffRole Role,
    string? DealerExternalId,
    string Reason);

public sealed record ChangeStaffRoleRequest(
    StaffRole Role,
    string? DealerExternalId,
    string Reason,
    uint RowVersion);

public sealed record ChangeStaffStateRequest(string Reason, uint RowVersion);

public sealed record StaffSessionSummary(string Status, StaffActor? User);

public interface IStaffAccessService
{
    Task<IReadOnlyList<StaffMemberSummary>> ListStaffAsync(StaffActor actor, CancellationToken cancellationToken);
    Task<IReadOnlyList<StaffInvitationSummary>> ListInvitationsAsync(StaffActor actor, CancellationToken cancellationToken);
    Task<StaffInvitationSummary> CreateInvitationAsync(CreateStaffInvitationRequest request, StaffActor actor, CancellationToken cancellationToken);
    Task<StaffMemberSummary> ChangeRoleAsync(Guid staffId, ChangeStaffRoleRequest request, StaffActor actor, CancellationToken cancellationToken);
    Task<StaffMemberSummary> SuspendAsync(Guid staffId, ChangeStaffStateRequest request, StaffActor actor, CancellationToken cancellationToken);
    Task<StaffMemberSummary> ReactivateAsync(Guid staffId, ChangeStaffStateRequest request, StaffActor actor, CancellationToken cancellationToken);
    Task<StaffActor?> ActivateInvitationAsync(Guid invitationId, string issuer, string subject, string email, CancellationToken cancellationToken);
}

public sealed class StaffAccessValidationException(string message) : Exception(message);
public sealed class StaffAccessConflictException(string message) : Exception(message);
public sealed class StaffAccessForbiddenException(string message) : Exception(message);
