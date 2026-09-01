using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WaterFlex.SaltMonitor.Domain.Security;
using WaterFlex.SaltMonitor.Infrastructure.Persistence;

namespace WaterFlex.SaltMonitor.Worker;

/// <summary>Configuration for provisioning staff identities into Cognito and Cloudflare Access, including the one-time bootstrap administrator.</summary>
public sealed class StaffProvisioningOptions
{
    public const string SectionName = "StaffProvisioning";
    public bool Enabled { get; set; }
    public string CognitoUserPoolId { get; set; } = string.Empty;
    public string CloudflareAccountId { get; set; } = string.Empty;
    public string CloudflareApiToken { get; set; } = string.Empty;
    public string CloudflareSecretId { get; set; } = "waterflex/staging/staff-provisioning";
    public string CloudflarePrivilegedGroupId { get; set; } = string.Empty;
    public string CloudflareDealerGroupId { get; set; } = string.Empty;
    public string BootstrapAdministratorEmail { get; set; } = string.Empty;
    public string BootstrapAdministratorDisplayName { get; set; } = "WaterFlex Administrator";
}

/// <summary>
/// Keeps Cloudflare Access group membership in sync with WaterFlex staff identities, creating the
/// privileged and dealer groups on first use if they don't already exist.
/// </summary>
public sealed class CloudflareStaffAccessGateway(HttpClient client, IOptions<StaffProvisioningOptions> options, IAmazonSecretsManager? secretsManager = null)
{
    /// <summary>Replaces the membership of the privileged and dealer Cloudflare Access groups with the given email sets.</summary>
    public async Task SynchronizeAsync(IReadOnlyCollection<string> privilegedEmails, IReadOnlyCollection<string> dealerEmails, CancellationToken ct)
    {
        var value = options.Value;
        EnsureConfigured(value);
        var apiToken = await ResolveApiTokenAsync(value, ct);
        client.BaseAddress ??= new Uri("https://api.cloudflare.com/client/v4/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        var privilegedGroupId = await ResolveGroupIdAsync(value.CloudflareAccountId, value.CloudflarePrivilegedGroupId, "WaterFlex-Privileged", ct);
        var dealerGroupId = await ResolveGroupIdAsync(value.CloudflareAccountId, value.CloudflareDealerGroupId, "WaterFlex-Dealer", ct);
        await UpdateGroupAsync(value.CloudflareAccountId, privilegedGroupId, privilegedEmails, ct);
        await UpdateGroupAsync(value.CloudflareAccountId, dealerGroupId, dealerEmails, ct);
    }

    private async Task<string> ResolveApiTokenAsync(StaffProvisioningOptions value, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(value.CloudflareApiToken)) return value.CloudflareApiToken;
        if (secretsManager is null || string.IsNullOrWhiteSpace(value.CloudflareSecretId))
            throw new InvalidOperationException("A Cloudflare API token or Secrets Manager secret ID is required.");
        var response = await secretsManager.GetSecretValueAsync(new GetSecretValueRequest { SecretId = value.CloudflareSecretId }, ct);
        var document = JsonNode.Parse(response.SecretString);
        return document?["CloudflareApiToken"]?.GetValue<string>() is { Length: > 0 } token
            ? token
            : throw new InvalidOperationException("The staff provisioning secret does not contain CloudflareApiToken.");
    }

    private async Task<string> ResolveGroupIdAsync(string accountId, string configuredId, string name, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(configuredId)) return configuredId;
        var path = $"accounts/{Uri.EscapeDataString(accountId)}/access/groups";
        using var listResponse = await client.GetAsync(path, ct);
        listResponse.EnsureSuccessStatusCode();
        var list = JsonNode.Parse(await listResponse.Content.ReadAsStringAsync(ct))?["result"]?.AsArray();
        var existing = list?.FirstOrDefault(item => string.Equals(item?["name"]?.GetValue<string>(), name, StringComparison.Ordinal));
        if (existing?["id"]?.GetValue<string>() is { Length: > 0 } existingId) return existingId;
        var placeholder = $"bootstrap-{name.ToLowerInvariant()}@invalid.waterflex.local";
        var payload = new JsonObject
        {
            ["name"] = name,
            ["include"] = new JsonArray(new JsonObject { ["email"] = new JsonObject { ["email"] = placeholder } }),
            ["exclude"] = new JsonArray(), ["require"] = new JsonArray()
        };
        using var createResponse = await client.PostAsync(path, new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"), ct);
        createResponse.EnsureSuccessStatusCode();
        return JsonNode.Parse(await createResponse.Content.ReadAsStringAsync(ct))?["result"]?["id"]?.GetValue<string>()
            ?? throw new InvalidOperationException($"Cloudflare did not return an ID for Access group '{name}'.");
    }

    private async Task UpdateGroupAsync(string accountId, string groupId, IReadOnlyCollection<string> emails, CancellationToken ct)
    {
        var path = $"accounts/{Uri.EscapeDataString(accountId)}/access/groups/{Uri.EscapeDataString(groupId)}";
        using var getResponse = await client.GetAsync(path, ct);
        getResponse.EnsureSuccessStatusCode();
        var document = JsonNode.Parse(await getResponse.Content.ReadAsStringAsync(ct));
        var result = document?["result"]?.AsObject() ?? throw new InvalidOperationException("Cloudflare Access group response did not include a result.");
        IEnumerable<string> synchronizedEmails = emails.Count > 0
            ? emails.Order(StringComparer.OrdinalIgnoreCase)
            : ["unassigned-waterflex-access@invalid.waterflex.local"];
        var include = new JsonArray(synchronizedEmails.Select(email => (JsonNode?)new JsonObject { ["email"] = new JsonObject { ["email"] = email } }).ToArray());
        var payload = new JsonObject
        {
            ["name"] = result["name"]?.DeepClone(), ["include"] = include,
            ["exclude"] = result["exclude"]?.DeepClone() ?? new JsonArray(),
            ["require"] = result["require"]?.DeepClone() ?? new JsonArray()
        };
        using var response = await client.PutAsync(path, new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"), ct);
        response.EnsureSuccessStatusCode();
    }

    private static void EnsureConfigured(StaffProvisioningOptions value)
    {
        if (string.IsNullOrWhiteSpace(value.CloudflareAccountId))
            throw new InvalidOperationException("Cloudflare staff provisioning configuration is incomplete.");
    }
}

/// <summary>
/// Drives the staff provisioning outbox: creates the first administrator invitation when the system
/// has no staff yet, and works through queued provisioning tasks against Cognito and Cloudflare with
/// retry backoff on failure.
/// </summary>
public sealed class StaffProvisioningProcessor(
    SaltMonitorDbContext dbContext,
    IAmazonCognitoIdentityProvider cognito,
    CloudflareStaffAccessGateway cloudflare,
    IOptions<StaffProvisioningOptions> options,
    TimeProvider timeProvider,
    ILogger<StaffProvisioningProcessor> logger)
{
    /// <summary>Creates the one-time bootstrap administrator invitation if no staff or invitations exist yet, so the system always has a way in.</summary>
    public async Task EnsureBootstrapInvitationAsync(CancellationToken ct)
    {
        var email = options.Value.BootstrapAdministratorEmail.Trim();
        if (string.IsNullOrWhiteSpace(email)) return;
        if (await dbContext.StaffIdentities.AnyAsync(ct) || await dbContext.StaffInvitations.AnyAsync(ct)) return;
        var now = timeProvider.GetUtcNow();
        var invitation = new StaffInvitation
        {
            Id = Guid.NewGuid(), Email = email, NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = options.Value.BootstrapAdministratorDisplayName.Trim(), Role = StaffRole.WaterFlexAdministrator,
            Status = StaffInvitationStatus.PendingProvisioning, CreatedByStaffId = "system:bootstrap",
            CreatedAtUtc = now, ExpiresAtUtc = now.AddDays(7)
        };
        dbContext.StaffInvitations.Add(invitation);
        dbContext.StaffProvisioningWorkItems.Add(new StaffProvisioningWorkItem
        {
            WorkType = "ProvisionInvitation", Status = StaffProvisioningWorkStatus.Pending, InvitationId = invitation.Id,
            IdempotencyKey = $"staff-invitation:{invitation.Id:D}", PayloadJson = JsonSerializer.Serialize(new { invitation.Id }),
            AvailableAtUtc = now, CreatedAtUtc = now
        });
        dbContext.StaffAccessAuditEvents.Add(new StaffAccessAuditEvent
        {
            EventType = "staff.bootstrap.created", ActorStaffId = "system:bootstrap", InvitationId = invitation.Id,
            Reason = "One-time configured first administrator bootstrap.", DetailsJson = JsonSerializer.Serialize(new { invitation.Email }), OccurredAtUtc = now
        });
        await dbContext.SaveChangesAsync(ct);
    }

    /// <summary>Claims and processes the next due provisioning work item, if any, with exponential backoff and eventual failure of the invitation after repeated attempts.</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled) return false;
        var now = timeProvider.GetUtcNow();
        var work = await dbContext.StaffProvisioningWorkItems
            .Where(item => (item.Status == StaffProvisioningWorkStatus.Pending || item.Status == StaffProvisioningWorkStatus.Failed) && item.AvailableAtUtc <= now)
            .OrderBy(item => item.Id).FirstOrDefaultAsync(ct);
        if (work is null) return false;
        work.Status = StaffProvisioningWorkStatus.Processing; work.AttemptCount++; work.LastError = null;
        await dbContext.SaveChangesAsync(ct);
        try
        {
            if (work.WorkType == "ProvisionInvitation") await ProvisionInvitationAsync(work, ct);
            else if (work.StaffIdentityId is { } staffId) await SynchronizeIdentityAsync(staffId, work.WorkType, ct);
            else throw new InvalidOperationException($"Unsupported staff provisioning work type '{work.WorkType}'.");
            work.Status = StaffProvisioningWorkStatus.Completed; work.CompletedAtUtc = timeProvider.GetUtcNow();
            await dbContext.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Staff provisioning work item {WorkItemId} failed on attempt {AttemptCount}", work.Id, work.AttemptCount);
            work.Status = StaffProvisioningWorkStatus.Failed; work.LastError = exception.Message;
            work.AvailableAtUtc = timeProvider.GetUtcNow().AddSeconds(Math.Min(900, Math.Pow(2, Math.Min(work.AttemptCount, 9)) * 5));
            if (work.AttemptCount >= 8 && work.InvitationId is { } invitationId)
            {
                var invitation = await dbContext.StaffInvitations.FindAsync([invitationId], ct);
                if (invitation is not null) { invitation.Status = StaffInvitationStatus.Failed; invitation.FailureReason = "External identity provisioning did not complete. An administrator can retry it."; }
            }
            await dbContext.SaveChangesAsync(ct);
            return true;
        }
    }

    /// <summary>Periodically re-syncs Cloudflare Access groups from current staff/invitation state, correcting drift from any provisioning steps that failed silently.</summary>
    public async Task ReconcileAsync(CancellationToken ct) => await SynchronizeCloudflareGroupsAsync(ct);

    private async Task ProvisionInvitationAsync(StaffProvisioningWorkItem work, CancellationToken ct)
    {
        var invitation = await dbContext.StaffInvitations.Include(item => item.Dealer).SingleAsync(item => item.Id == work.InvitationId, ct);
        await EnsureCognitoUserAsync(invitation, ct);
        await SynchronizeCloudflareGroupsAsync(ct);
        invitation.Status = StaffInvitationStatus.Ready; invitation.FailureReason = null;
    }

    private async Task EnsureCognitoUserAsync(StaffInvitation invitation, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.Value.CognitoUserPoolId)) throw new InvalidOperationException("StaffProvisioning:CognitoUserPoolId is required.");
        try
        {
            await cognito.AdminGetUserAsync(new AdminGetUserRequest { UserPoolId = options.Value.CognitoUserPoolId, Username = invitation.Email }, ct);
        }
        catch (UserNotFoundException)
        {
            await cognito.AdminCreateUserAsync(new AdminCreateUserRequest
            {
                UserPoolId = options.Value.CognitoUserPoolId, Username = invitation.Email,
                DesiredDeliveryMediums = [DeliveryMediumType.EMAIL],
                UserAttributes = [new AttributeType { Name = "email", Value = invitation.Email }, new AttributeType { Name = "name", Value = invitation.DisplayName }]
            }, ct);
        }
    }

    private async Task SynchronizeIdentityAsync(Guid staffId, string workType, CancellationToken ct)
    {
        var staff = await dbContext.StaffIdentities.SingleAsync(item => item.Id == staffId, ct);
        if (workType == "SuspendIdentity")
        {
            await DisableAndSignOutAsync(staff.Email, ct);
            await SynchronizeCloudflareGroupsAsync(ct);
            return;
        }
        if (workType is "SynchronizeRole" or "ReactivateIdentity")
        {
            await cognito.AdminEnableUserAsync(new AdminEnableUserRequest { UserPoolId = options.Value.CognitoUserPoolId, Username = staff.Email }, ct);
            await SynchronizeCloudflareGroupsAsync(ct, staff);
            await cognito.AdminUserGlobalSignOutAsync(new AdminUserGlobalSignOutRequest { UserPoolId = options.Value.CognitoUserPoolId, Username = staff.Email }, ct);
            staff.State = StaffIdentityState.Active; staff.IsActive = true; staff.SuspendedAtUtc = null; staff.UpdatedAtUtc = timeProvider.GetUtcNow();
        }
    }

    private async Task DisableAndSignOutAsync(string username, CancellationToken ct)
    {
        await cognito.AdminDisableUserAsync(new AdminDisableUserRequest { UserPoolId = options.Value.CognitoUserPoolId, Username = username }, ct);
        await cognito.AdminUserGlobalSignOutAsync(new AdminUserGlobalSignOutRequest { UserPoolId = options.Value.CognitoUserPoolId, Username = username }, ct);
    }

    private async Task SynchronizeCloudflareGroupsAsync(CancellationToken ct, StaffIdentityRecord? pendingActivation = null)
    {
        var identityEmails = await dbContext.StaffIdentities.AsNoTracking().Where(item => item.IsActive).Select(item => new { item.Email, item.Role }).ToListAsync(ct);
        var invitationEmails = await dbContext.StaffInvitations.AsNoTracking().Where(item => item.Status == StaffInvitationStatus.PendingProvisioning || item.Status == StaffInvitationStatus.Ready).Select(item => new { item.Email, item.Role }).ToListAsync(ct);
        var all = identityEmails.Concat(invitationEmails).ToList();
        if (pendingActivation is not null) all.Add(new { pendingActivation.Email, pendingActivation.Role });
        await cloudflare.SynchronizeAsync(
            all.Where(item => item.Role.RequiresPrivilegedAccessTier()).Select(item => item.Email).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            all.Where(item => !item.Role.RequiresPrivilegedAccessTier()).Select(item => item.Email).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), ct);
    }
}

/// <summary>Background loop that runs staff provisioning work and periodic Cloudflare reconciliation; a no-op when staff provisioning is disabled.</summary>
public sealed class StaffProvisioningWorker(IServiceScopeFactory scopeFactory, IOptions<StaffProvisioningOptions> options, ILogger<StaffProvisioningWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) { logger.LogInformation("Staff provisioning worker is disabled"); return; }
        var nextReconciliation = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<StaffProvisioningProcessor>();
                await processor.EnsureBootstrapInvitationAsync(stoppingToken);
                var processed = await processor.ProcessNextAsync(stoppingToken);
                if (timeProviderNow() >= nextReconciliation) { await processor.ReconcileAsync(stoppingToken); nextReconciliation = timeProviderNow().AddMinutes(15); }
                if (!processed) await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogError(exception, "Staff provisioning worker iteration failed"); await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        }
        static DateTimeOffset timeProviderNow() => DateTimeOffset.UtcNow;
    }
}
