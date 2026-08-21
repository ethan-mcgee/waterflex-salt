using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WaterFlex.SaltMonitor.Domain.Security;
using WaterFlex.SaltMonitor.Operations;

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence;

public sealed class EfStaffAccessService(SaltMonitorDbContext dbContext, TimeProvider timeProvider) : IStaffAccessService
{
    public async Task<IReadOnlyList<StaffMemberSummary>> ListStaffAsync(StaffActor actor, CancellationToken cancellationToken)
    {
        EnsureCanAdminister(actor);
        var query = Scope(dbContext.StaffIdentities.AsNoTracking().Include(staff => staff.Dealer), actor);
        var staff = await query.OrderBy(item => item.DisplayName).ToListAsync(cancellationToken);
        return staff.Select(ToSummary).ToList();
    }

    public async Task<IReadOnlyList<StaffInvitationSummary>> ListInvitationsAsync(StaffActor actor, CancellationToken cancellationToken)
    {
        EnsureCanAdminister(actor);
        var query = dbContext.StaffInvitations.AsNoTracking().Include(invitation => invitation.Dealer).AsQueryable();
        if (actor.Role == StaffRole.DealerAdministrator)
        {
            query = query.Where(invitation => invitation.Dealer != null && invitation.Dealer.ExternalId == actor.DealerExternalId);
        }
        var invitations = await query.OrderByDescending(invitation => invitation.CreatedAtUtc).ToListAsync(cancellationToken);
        return invitations.Select(ToSummary).ToList();
    }

    public async Task<StaffInvitationSummary> CreateInvitationAsync(CreateStaffInvitationRequest request, StaffActor actor, CancellationToken cancellationToken)
    {
        EnsureCanAdminister(actor);
        var email = request.Email.Trim();
        var normalizedEmail = NormalizeEmail(email);
        var displayName = request.DisplayName.Trim();
        var reason = RequireReason(request.Reason);
        if (displayName.Length is < 1 or > 200) throw new StaffAccessValidationException("Display name is required and must be 200 characters or fewer.");
        if (request.Role.RequiresDealer() != !string.IsNullOrWhiteSpace(request.DealerExternalId)) throw new StaffAccessValidationException("Dealer roles require a dealer and WaterFlex roles cannot have one.");
        EnsureRoleAssignmentAllowed(actor, request.Role, request.DealerExternalId);

        var existingIdentity = await dbContext.StaffIdentities.AnyAsync(staff => staff.NormalizedEmail == normalizedEmail, cancellationToken);
        var existingInvitation = await dbContext.StaffInvitations.AnyAsync(invitation => invitation.NormalizedEmail == normalizedEmail && (invitation.Status == StaffInvitationStatus.PendingProvisioning || invitation.Status == StaffInvitationStatus.Ready), cancellationToken);
        if (existingIdentity || existingInvitation) throw new StaffAccessConflictException("An identity or active invitation already exists for this email address.");

        Dealer? dealer = null;
        if (request.Role.RequiresDealer())
        {
            dealer = await dbContext.Dealers.SingleOrDefaultAsync(item => item.ExternalId == request.DealerExternalId, cancellationToken)
                ?? throw new StaffAccessValidationException("Dealer was not found.");
        }

        var now = timeProvider.GetUtcNow();
        var invitation = new StaffInvitation
        {
            Id = Guid.NewGuid(), Email = email, NormalizedEmail = normalizedEmail, DisplayName = displayName,
            Role = request.Role, DealerId = dealer?.Id, Status = StaffInvitationStatus.PendingProvisioning,
            CreatedByStaffId = actor.UserId, CreatedAtUtc = now, ExpiresAtUtc = now.AddDays(7)
        };
        dbContext.StaffInvitations.Add(invitation);
        AddAudit("staff.invitation.created", actor, null, invitation.Id, reason, new { invitation.Email, invitation.Role, Dealer = dealer?.ExternalId });
        dbContext.StaffProvisioningWorkItems.Add(new StaffProvisioningWorkItem
        {
            WorkType = "ProvisionInvitation", Status = StaffProvisioningWorkStatus.Pending,
            InvitationId = invitation.Id, IdempotencyKey = $"staff-invitation:{invitation.Id:D}",
            PayloadJson = JsonSerializer.Serialize(new { invitation.Id }), AvailableAtUtc = now, CreatedAtUtc = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        invitation.Dealer = dealer;
        return ToSummary(invitation);
    }

    public async Task<StaffMemberSummary> ChangeRoleAsync(Guid staffId, ChangeStaffRoleRequest request, StaffActor actor, CancellationToken cancellationToken)
    {
        EnsureCanAdminister(actor);
        var staff = await LoadScopedStaffAsync(staffId, actor, cancellationToken);
        if (staff.RowVersion != request.RowVersion) throw new StaffAccessConflictException("The staff record changed; refresh before retrying.");
        EnsureRoleAssignmentAllowed(actor, request.Role, request.DealerExternalId);
        await ProtectLastAdministratorAsync(staff, request.Role, cancellationToken);
        var dealer = await ResolveDealerAsync(request.Role, request.DealerExternalId, cancellationToken);
        var before = new { staff.Role, Dealer = staff.Dealer?.ExternalId };
        staff.Role = request.Role; staff.DealerId = dealer?.Id; staff.Dealer = dealer; staff.UpdatedAtUtc = timeProvider.GetUtcNow();
        QueueStaffSync(staff, "SynchronizeRole");
        AddAudit("staff.role.changed", actor, staff.Id, null, RequireReason(request.Reason), new { Before = before, After = new { staff.Role, Dealer = dealer?.ExternalId } });
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToSummary(staff);
    }

    public Task<StaffMemberSummary> SuspendAsync(Guid staffId, ChangeStaffStateRequest request, StaffActor actor, CancellationToken cancellationToken) =>
        ChangeStateAsync(staffId, StaffIdentityState.Suspended, request, actor, cancellationToken);

    public Task<StaffMemberSummary> ReactivateAsync(Guid staffId, ChangeStaffStateRequest request, StaffActor actor, CancellationToken cancellationToken) =>
        ChangeStateAsync(staffId, StaffIdentityState.Active, request, actor, cancellationToken);

    public async Task<StaffActor?> ActivateInvitationAsync(Guid invitationId, string issuer, string subject, string email, CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(email);
        var invitation = await dbContext.StaffInvitations.Include(item => item.Dealer).SingleOrDefaultAsync(item => item.Id == invitationId && item.NormalizedEmail == normalizedEmail, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (invitation is null || invitation.Status != StaffInvitationStatus.Ready || invitation.ExpiresAtUtc <= now) return null;
        if (await dbContext.StaffIdentities.AnyAsync(item => item.Issuer == issuer && item.Subject == subject, cancellationToken)) return null;
        var identity = new StaffIdentityRecord
        {
            Id = Guid.NewGuid(), Issuer = issuer, Subject = subject, Email = invitation.Email, NormalizedEmail = normalizedEmail,
            DisplayName = invitation.DisplayName, Role = invitation.Role, DealerId = invitation.DealerId,
            IsActive = true, State = StaffIdentityState.Active, CreatedAtUtc = now, UpdatedAtUtc = now, ActivatedAtUtc = now
        };
        dbContext.StaffIdentities.Add(identity);
        invitation.Status = StaffInvitationStatus.Accepted; invitation.AcceptedAtUtc = now; invitation.AcceptedStaffIdentityId = identity.Id;
        AddAudit("staff.invitation.accepted", new StaffActor(identity.Id.ToString("D"), identity.DisplayName, identity.Role, invitation.Dealer?.ExternalId, invitation.Dealer?.DisplayName), identity.Id, invitation.Id, "Invitation accepted by authenticated email owner.", new { invitation.Email });
        await dbContext.SaveChangesAsync(cancellationToken);
        return new StaffActor(identity.Id.ToString("D"), identity.DisplayName, identity.Role, invitation.Dealer?.ExternalId, invitation.Dealer?.DisplayName);
    }

    private async Task<StaffMemberSummary> ChangeStateAsync(Guid staffId, StaffIdentityState state, ChangeStaffStateRequest request, StaffActor actor, CancellationToken cancellationToken)
    {
        EnsureCanAdminister(actor);
        var staff = await LoadScopedStaffAsync(staffId, actor, cancellationToken);
        if (staff.RowVersion != request.RowVersion) throw new StaffAccessConflictException("The staff record changed; refresh before retrying.");
        if (state == StaffIdentityState.Suspended) await ProtectLastAdministratorAsync(staff, null, cancellationToken);
        var requestedState = state;
        staff.State = state;
        staff.IsActive = state == StaffIdentityState.Active;
        staff.UpdatedAtUtc = timeProvider.GetUtcNow();
        staff.SuspendedAtUtc = requestedState == StaffIdentityState.Suspended ? staff.UpdatedAtUtc : null;
        QueueStaffSync(staff, state == StaffIdentityState.Active ? "ReactivateIdentity" : "SuspendIdentity");
        AddAudit(requestedState == StaffIdentityState.Active ? "staff.reactivation.requested" : "staff.suspended", actor, staff.Id, null, RequireReason(request.Reason), new { RequestedState = requestedState });
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToSummary(staff);
    }

    private static IQueryable<StaffIdentityRecord> Scope(IQueryable<StaffIdentityRecord> query, StaffActor actor) => actor.Role == StaffRole.DealerAdministrator
        ? query.Where(staff => staff.Dealer != null && staff.Dealer.ExternalId == actor.DealerExternalId && (staff.Role == StaffRole.DealerAdministrator || staff.Role == StaffRole.DealerTechnician))
        : query;
    private async Task<StaffIdentityRecord> LoadScopedStaffAsync(Guid id, StaffActor actor, CancellationToken ct) =>
        await Scope(dbContext.StaffIdentities.Include(staff => staff.Dealer), actor).SingleOrDefaultAsync(staff => staff.Id == id, ct)
        ?? throw new StaffAccessForbiddenException("Staff identity is outside the administrator's scope.");
    private static void EnsureCanAdminister(StaffActor actor)
    {
        if (!actor.Role.HasCapability(StaffCapability.WaterFlexStaffAdministration) && !actor.Role.HasCapability(StaffCapability.DealerStaffAdministration))
            throw new StaffAccessForbiddenException("Staff administration capability is required.");
    }
    private static void EnsureRoleAssignmentAllowed(StaffActor actor, StaffRole role, string? dealerExternalId)
    {
        if (actor.Role == StaffRole.DealerAdministrator && (role is not (StaffRole.DealerAdministrator or StaffRole.DealerTechnician) || !string.Equals(actor.DealerExternalId, dealerExternalId, StringComparison.OrdinalIgnoreCase)))
            throw new StaffAccessForbiddenException("Dealer administrators can only manage roles within their own dealer.");
    }
    private async Task ProtectLastAdministratorAsync(StaffIdentityRecord staff, StaffRole? replacement, CancellationToken ct)
    {
        if (staff.Role != StaffRole.WaterFlexAdministrator || replacement == StaffRole.WaterFlexAdministrator) return;
        if (await dbContext.StaffIdentities.CountAsync(item => item.Role == StaffRole.WaterFlexAdministrator && item.IsActive, ct) <= 1)
            throw new StaffAccessConflictException("The last active WaterFlex administrator cannot be removed or suspended.");
    }
    private async Task<Dealer?> ResolveDealerAsync(StaffRole role, string? dealerExternalId, CancellationToken ct)
    {
        if (role.RequiresDealer() != !string.IsNullOrWhiteSpace(dealerExternalId)) throw new StaffAccessValidationException("Dealer roles require a dealer and WaterFlex roles cannot have one.");
        return role.RequiresDealer() ? await dbContext.Dealers.SingleOrDefaultAsync(item => item.ExternalId == dealerExternalId, ct) ?? throw new StaffAccessValidationException("Dealer was not found.") : null;
    }
    private void QueueStaffSync(StaffIdentityRecord staff, string type)
    {
        var now = timeProvider.GetUtcNow();
        dbContext.StaffProvisioningWorkItems.Add(new StaffProvisioningWorkItem { WorkType = type, Status = StaffProvisioningWorkStatus.Pending, StaffIdentityId = staff.Id, IdempotencyKey = $"{type}:{staff.Id:D}:{now.ToUnixTimeMilliseconds()}", PayloadJson = JsonSerializer.Serialize(new { staff.Id }), AvailableAtUtc = now, CreatedAtUtc = now });
    }
    private void AddAudit(string type, StaffActor actor, Guid? target, Guid? invitation, string reason, object details) => dbContext.StaffAccessAuditEvents.Add(new StaffAccessAuditEvent { EventType = type, ActorStaffId = actor.UserId, TargetStaffIdentityId = target, InvitationId = invitation, Reason = reason, DetailsJson = JsonSerializer.Serialize(details), OccurredAtUtc = timeProvider.GetUtcNow() });
    private static string NormalizeEmail(string email)
    {
        try { return new System.Net.Mail.MailAddress(email).Address.Trim().ToUpperInvariant(); }
        catch { throw new StaffAccessValidationException("A valid email address is required."); }
    }
    private static string RequireReason(string reason) => string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500 ? throw new StaffAccessValidationException("A reason between 1 and 500 characters is required.") : reason.Trim();
    private static StaffMemberSummary ToSummary(StaffIdentityRecord staff) => new(staff.Id, staff.Email, staff.DisplayName, staff.Role, staff.Dealer?.ExternalId, staff.Dealer?.DisplayName, staff.State, staff.CreatedAtUtc, staff.UpdatedAtUtc, staff.RowVersion);
    private static StaffInvitationSummary ToSummary(StaffInvitation invitation) => new(invitation.Id, invitation.Email, invitation.DisplayName, invitation.Role, invitation.Dealer?.ExternalId, invitation.Dealer?.DisplayName, invitation.Status, invitation.CreatedAtUtc, invitation.ExpiresAtUtc, invitation.FailureReason);
}
