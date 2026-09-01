using WaterFlex.SaltMonitor.Domain.Security;

namespace WaterFlex.SaltMonitor.Operations;

/// <summary>A staff account as shown in the staff access administration list.</summary>
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

/// <summary>A pending or resolved invitation for a prospective staff member to join, as shown in the invitations list.</summary>
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

/// <summary>Request to invite a new staff member. <see cref="Reason"/> is recorded for audit purposes since staff access changes are privileged.</summary>
public sealed record CreateStaffInvitationRequest(
    string Email,
    string DisplayName,
    StaffRole Role,
    string? DealerExternalId,
    string Reason);

/// <summary>Request to change an existing staff member's role or dealer scope. <see cref="RowVersion"/> guards against concurrent edits.</summary>
public sealed record ChangeStaffRoleRequest(
    StaffRole Role,
    string? DealerExternalId,
    string Reason,
    uint RowVersion);

/// <summary>Request to suspend or reactivate a staff member.</summary>
public sealed record ChangeStaffStateRequest(string Reason, uint RowVersion);

/// <summary>The current caller's session state, used by the frontend to know whether it is looking at an authenticated staff user.</summary>
public sealed record StaffSessionSummary(string Status, StaffActor? User);

/// <summary>Administers staff accounts and invitations: who can access operations tooling and at what role.</summary>
public interface IStaffAccessService
{
    Task<IReadOnlyList<StaffMemberSummary>> ListStaffAsync(StaffActor actor, CancellationToken cancellationToken);
    Task<IReadOnlyList<StaffInvitationSummary>> ListInvitationsAsync(StaffActor actor, CancellationToken cancellationToken);
    Task<StaffInvitationSummary> CreateInvitationAsync(CreateStaffInvitationRequest request, StaffActor actor, CancellationToken cancellationToken);
    Task<StaffMemberSummary> ChangeRoleAsync(Guid staffId, ChangeStaffRoleRequest request, StaffActor actor, CancellationToken cancellationToken);
    Task<StaffMemberSummary> SuspendAsync(Guid staffId, ChangeStaffStateRequest request, StaffActor actor, CancellationToken cancellationToken);
    Task<StaffMemberSummary> ReactivateAsync(Guid staffId, ChangeStaffStateRequest request, StaffActor actor, CancellationToken cancellationToken);

    /// <summary>Redeems a pending invitation once the invited user has authenticated, turning it into an active staff identity.</summary>
    Task<StaffActor?> ActivateInvitationAsync(Guid invitationId, string issuer, string subject, string email, CancellationToken cancellationToken);
}

/// <summary>Thrown when a staff access request fails input validation.</summary>
public sealed class StaffAccessValidationException(string message) : Exception(message);

/// <summary>Thrown when a staff access request conflicts with current state (e.g. a stale row version or duplicate invitation).</summary>
public sealed class StaffAccessConflictException(string message) : Exception(message);

/// <summary>Thrown when the acting staff member lacks permission for the requested change.</summary>
public sealed class StaffAccessForbiddenException(string message) : Exception(message);
