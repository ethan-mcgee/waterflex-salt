using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WaterFlex.SaltMonitor.Domain.Security;
using WaterFlex.SaltMonitor.Ingestion;
using WaterFlex.SaltMonitor.Provisioning;

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence;

public sealed class EfCommissioningSessionService(
    SaltMonitorDbContext dbContext,
    IWaterFlexCustomerDirectory customerDirectory,
    IInstallationWorkOrderDirectory workOrderDirectory,
    TimeProvider timeProvider) : ICommissioningSessionService
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(30);
    private static readonly CommissioningSessionStatus[] LiveStatuses =
    [
        CommissioningSessionStatus.PendingSensor,
        CommissioningSessionStatus.ActivatedAwaitingHealth,
        CommissioningSessionStatus.AwaitingFirstTelemetry
    ];

    public async Task<InstallationWorkOrderView?> FindWorkOrderAsync(
        string workOrderNumber,
        StaffActor technician,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidTechnician(technician) || string.IsNullOrWhiteSpace(workOrderNumber))
        {
            return null;
        }

        var workOrder = await workOrderDirectory.FindEligibleAsync(
            workOrderNumber,
            technician.DealerExternalId!,
            cancellationToken);
        return workOrder is null
            ? null
            : new(
                workOrder.WorkOrderNumber,
                workOrder.CustomerDisplayName,
                workOrder.LocationDisplayName,
                workOrder.AddressSummary,
                workOrder.TankLocation);
    }

    public async Task<CommissioningSessionResult> CreateFromWorkOrderAsync(
        CreateWorkOrderCommissioningSessionRequest request,
        StaffActor technician,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidTechnician(technician))
        {
            return CommissioningSessionResult.Failed(CommissioningSessionFailure.InvalidTechnician);
        }

        var workOrder = await workOrderDirectory.FindEligibleAsync(
            request.WorkOrderNumber,
            technician.DealerExternalId!,
            cancellationToken);
        if (workOrder is null)
        {
            return CommissioningSessionResult.Failed(CommissioningSessionFailure.WorkOrderNotFound);
        }

        var tankLocation = string.IsNullOrWhiteSpace(workOrder.TankLocation)
            ? request.TankLocation?.Trim()
            : workOrder.TankLocation;
        if (string.IsNullOrWhiteSpace(tankLocation))
        {
            return CommissioningSessionResult.Failed(
                CommissioningSessionFailure.TankLocationRequired,
                [new(nameof(request.TankLocation), "Tank location is required because it is missing from the work order.")]);
        }

        var normalized = new CreateCommissioningSessionRequest(
            workOrder.WaterFlexCustomerId,
            workOrder.WaterFlexLocationId,
            workOrder.WaterFlexAssetId,
            request.SerialNumber,
            request.WorkOrderNumber,
            request.TankDepthCm);

        return await CreateWithSelectionAsync(
            normalized,
            new WaterFlexCommissioningSelection(
                workOrder.WaterFlexCustomerId,
                string.Empty,
                workOrder.CustomerDisplayName,
                workOrder.WaterFlexLocationId,
                workOrder.LocationDisplayName,
                workOrder.AddressSummary,
                workOrder.WaterFlexAssetId,
                tankLocation,
                null),
            technician,
            cancellationToken);
    }

    public async Task<CommissioningSessionResult> CreateAsync(
        CreateCommissioningSessionRequest request,
        StaffActor technician,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidTechnician(technician))
        {
            return CommissioningSessionResult.Failed(CommissioningSessionFailure.InvalidTechnician);
        }

        var normalized = Normalize(request);
        var validationErrors = Validate(normalized);
        if (validationErrors.Count > 0)
        {
            return CommissioningSessionResult.Failed(
                CommissioningSessionFailure.InvalidRequest,
                validationErrors);
        }

        var selection = await customerDirectory.ResolveAsync(
            normalized.WaterFlexCustomerId,
            normalized.WaterFlexLocationId,
            normalized.WaterFlexAssetId,
            cancellationToken);
        if (selection is null)
        {
            return CommissioningSessionResult.Failed(
                CommissioningSessionFailure.DirectorySelectionNotFound);
        }

        return await CreateWithSelectionAsync(normalized, selection, technician, cancellationToken);
    }

    private async Task<CommissioningSessionResult> CreateWithSelectionAsync(
        CreateCommissioningSessionRequest request,
        WaterFlexCommissioningSelection selection,
        StaffActor technician,
        CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(() => CreateWithinTransactionAsync(
                request,
                selection,
                technician,
                cancellationToken));
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            return CommissioningSessionResult.Failed(CommissioningSessionFailure.Conflict);
        }
    }

    public async Task<CommissioningSessionResult> GetAsync(
        Guid sessionId,
        StaffActor technician,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidTechnician(technician))
        {
            return CommissioningSessionResult.Failed(CommissioningSessionFailure.InvalidTechnician);
        }

        var session = await LoadScopedSessionAsync(sessionId, technician, cancellationToken);
        if (session is null)
        {
            return CommissioningSessionResult.Failed(CommissioningSessionFailure.SessionNotFound);
        }

        if (ExpireIfNeeded(session, timeProvider.GetUtcNow(), "system"))
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return CommissioningSessionResult.Success(ToView(session));
    }

    public async Task<CommissioningSessionResult> CancelAsync(
        Guid sessionId,
        StaffActor technician,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidTechnician(technician))
        {
            return CommissioningSessionResult.Failed(CommissioningSessionFailure.InvalidTechnician);
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var session = await LoadScopedSessionAsync(sessionId, technician, cancellationToken);
            if (session is null)
            {
                return CommissioningSessionResult.Failed(CommissioningSessionFailure.SessionNotFound);
            }

            var now = timeProvider.GetUtcNow();
            if (ExpireIfNeeded(session, now, "system"))
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return CommissioningSessionResult.Failed(
                    CommissioningSessionFailure.SessionUnavailable);
            }

            if (session.Status != CommissioningSessionStatus.PendingSensor)
            {
                return CommissioningSessionResult.Failed(
                    CommissioningSessionFailure.SessionUnavailable);
            }

            session.Status = CommissioningSessionStatus.Cancelled;
            session.CancelledAtUtc = now;
            if (session.Device.Status == DeviceLifecycleStatus.Commissioning)
            {
                session.Device.Status = DeviceLifecycleStatus.Registered;
            }
            session.AuditEvents.Add(CreateAudit(
                session,
                "commissioning_session_cancelled",
                "technician",
                technician.UserId,
                now,
                new { technician.DisplayName }));

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return CommissioningSessionResult.Success(ToView(session));
        });
    }

    private async Task<CommissioningSessionResult> CreateWithinTransactionAsync(
        CreateCommissioningSessionRequest request,
        WaterFlexCommissioningSelection selection,
        StaffActor technician,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var now = timeProvider.GetUtcNow();
        var device = await dbContext.Devices
            .Include(candidate => candidate.BootstrapCredentials)
            .SingleOrDefaultAsync(
                candidate => candidate.SerialNumber == request.SerialNumber,
                cancellationToken);
        if (device is null)
        {
            return CommissioningSessionResult.Failed(CommissioningSessionFailure.DeviceNotFound);
        }

        await ExpireSessionsAsync(
            session => session.DeviceId == device.Id,
            now,
            cancellationToken);

        if (device.Status != DeviceLifecycleStatus.Registered
            || !device.BootstrapCredentials.Any(credential =>
                credential.RevokedAtUtc == null
                && credential.ConsumedAtUtc == null
                && credential.ValidFromUtc <= now
                && (credential.ExpiresAtUtc == null || credential.ExpiresAtUtc > now)))
        {
            return CommissioningSessionResult.Failed(CommissioningSessionFailure.DeviceUnavailable);
        }

        var dealer = await UpsertDealerAsync(technician, cancellationToken);
        var customer = await UpsertCustomerAsync(selection, now, cancellationToken);
        var location = await UpsertLocationAsync(customer, selection, now, cancellationToken);
        var tank = await UpsertTankAsync(location, selection, cancellationToken);

        await ExpireSessionsAsync(
            session => session.TankId == tank.Id,
            now,
            cancellationToken);

        if (await dbContext.DeviceInstallations.AnyAsync(
            installation => installation.TankId == tank.Id && installation.RemovedAtUtc == null,
            cancellationToken))
        {
            return CommissioningSessionResult.Failed(CommissioningSessionFailure.TankUnavailable);
        }

        if (await dbContext.CommissioningSessions.AnyAsync(
            session => (session.DeviceId == device.Id || session.TankId == tank.Id)
                && LiveStatuses.Contains(session.Status),
            cancellationToken))
        {
            return CommissioningSessionResult.Failed(
                CommissioningSessionFailure.DeviceAlreadyReserved);
        }

        device.Status = DeviceLifecycleStatus.Commissioning;
        var session = new CommissioningSession
        {
            Id = Guid.NewGuid(),
            DeviceId = device.Id,
            DealerId = dealer.Id,
            TankId = tank.Id,
            Status = CommissioningSessionStatus.PendingSensor,
            TankDepthMm = CentimetersToMillimeters(request.TankDepthCm),
            WaterFlexWorkOrderId = request.WaterFlexWorkOrderId,
            CreatedByActorId = technician.UserId,
            CreatedByDisplayName = technician.DisplayName,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(SessionLifetime),
            Device = device,
            Dealer = dealer,
            Tank = tank
        };
        session.AuditEvents.Add(CreateAudit(
            session,
            "commissioning_session_created",
            "technician",
            technician.UserId,
            now,
            new
            {
                technician.DisplayName,
                DealerExternalId = technician.DealerExternalId,
                TankDepthMm = session.TankDepthMm
            }));

        dbContext.CommissioningSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CommissioningSessionResult.Success(new(
            session.Id,
            device.Id,
            device.SerialNumber,
            session.Status,
            session.CreatedAtUtc,
            session.ExpiresAtUtc,
            dealer.DisplayName,
            selection.CustomerDisplayName,
            selection.LocationDisplayName,
            selection.AddressSummary,
            selection.TankLabel,
            MillimetersToCentimeters(session.TankDepthMm),
            null,
            null,
            null));
    }

    private async Task<CommissioningSession?> LoadScopedSessionAsync(
        Guid sessionId,
        StaffActor technician,
        CancellationToken cancellationToken) =>
        await dbContext.CommissioningSessions
            .Include(session => session.Device)
            .Include(session => session.Dealer)
            .Include(session => session.ProvisionalCredential)
            .Include(session => session.AuditEvents)
            .Include(session => session.Tank)
                .ThenInclude(tank => tank.ServiceLocation)
                    .ThenInclude(location => location.CustomerAccount)
            .SingleOrDefaultAsync(
                session => session.Id == sessionId
                    && session.Dealer.ExternalId == technician.DealerExternalId,
                cancellationToken);

    private async Task ExpireSessionsAsync(
        System.Linq.Expressions.Expression<Func<CommissioningSession, bool>> predicate,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var sessions = await dbContext.CommissioningSessions
            .Where(predicate)
            .Where(session => LiveStatuses.Contains(session.Status) && session.ExpiresAtUtc <= now)
            .Include(session => session.Device)
            .Include(session => session.ProvisionalCredential)
            .Include(session => session.AuditEvents)
            .ToArrayAsync(cancellationToken);

        foreach (var session in sessions)
        {
            ExpireIfNeeded(session, now, "system");
        }

        if (sessions.Length > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static bool ExpireIfNeeded(
        CommissioningSession session,
        DateTimeOffset now,
        string actorId)
    {
        if (!LiveStatuses.Contains(session.Status) || session.ExpiresAtUtc > now)
        {
            return false;
        }

        session.Status = CommissioningSessionStatus.Expired;
        if (session.ProvisionalCredential is { RevokedAtUtc: null } credential)
        {
            credential.RevokedAtUtc = now;
            session.Device.Status = DeviceLifecycleStatus.Quarantined;
        }
        else if (session.Device.Status == DeviceLifecycleStatus.Commissioning)
        {
            session.Device.Status = DeviceLifecycleStatus.Registered;
        }
        session.AuditEvents.Add(CreateAudit(
            session,
            "commissioning_session_expired",
            "system",
            actorId,
            now,
            new { }));
        return true;
    }

    private async Task<Dealer> UpsertDealerAsync(
        StaffActor technician,
        CancellationToken cancellationToken)
    {
        var dealer = await dbContext.Dealers.SingleOrDefaultAsync(
            candidate => candidate.ExternalId == technician.DealerExternalId,
            cancellationToken);
        if (dealer is null)
        {
            dealer = new Dealer
            {
                Id = Guid.NewGuid(),
                ExternalId = technician.DealerExternalId!,
                DisplayName = technician.DealerName!,
                IsActive = true
            };
            dbContext.Dealers.Add(dealer);
        }
        else
        {
            dealer.DisplayName = technician.DealerName!;
            dealer.IsActive = true;
        }
        return dealer;
    }

    private async Task<CustomerAccount> UpsertCustomerAsync(
        WaterFlexCommissioningSelection selection,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var customer = await dbContext.CustomerAccounts.SingleOrDefaultAsync(
            candidate => candidate.WaterFlexCustomerId == selection.WaterFlexCustomerId,
            cancellationToken);
        if (customer is null)
        {
            customer = new CustomerAccount
            {
                Id = Guid.NewGuid(),
                WaterFlexCustomerId = selection.WaterFlexCustomerId,
                AccountNumber = selection.AccountNumber,
                DisplayName = selection.CustomerDisplayName,
                IsActive = true,
                LastSyncedAtUtc = now
            };
            dbContext.CustomerAccounts.Add(customer);
        }
        else
        {
            customer.AccountNumber = selection.AccountNumber;
            customer.DisplayName = selection.CustomerDisplayName;
            customer.IsActive = true;
            customer.LastSyncedAtUtc = now;
        }
        return customer;
    }

    private async Task<ServiceLocation> UpsertLocationAsync(
        CustomerAccount customer,
        WaterFlexCommissioningSelection selection,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var location = await dbContext.ServiceLocations.SingleOrDefaultAsync(
            candidate => candidate.CustomerAccountId == customer.Id
                && candidate.WaterFlexLocationId == selection.WaterFlexLocationId,
            cancellationToken);
        if (location is null)
        {
            location = new ServiceLocation
            {
                Id = Guid.NewGuid(),
                CustomerAccountId = customer.Id,
                WaterFlexLocationId = selection.WaterFlexLocationId,
                DisplayName = selection.LocationDisplayName,
                AddressSummary = selection.AddressSummary,
                IsActive = true,
                LastSyncedAtUtc = now
            };
            dbContext.ServiceLocations.Add(location);
        }
        else
        {
            location.DisplayName = selection.LocationDisplayName;
            location.AddressSummary = selection.AddressSummary;
            location.IsActive = true;
            location.LastSyncedAtUtc = now;
        }
        return location;
    }

    private async Task<Tank> UpsertTankAsync(
        ServiceLocation location,
        WaterFlexCommissioningSelection selection,
        CancellationToken cancellationToken)
    {
        var tank = await dbContext.Tanks.SingleOrDefaultAsync(
            candidate => candidate.ServiceLocationId == location.Id
                && candidate.WaterFlexAssetId == selection.WaterFlexAssetId,
            cancellationToken);
        if (tank is null)
        {
            tank = new Tank
            {
                Id = Guid.NewGuid(),
                ServiceLocationId = location.Id,
                WaterFlexAssetId = selection.WaterFlexAssetId,
                Label = selection.TankLabel,
                CapacityPounds = selection.CapacityPounds,
                IsActive = true,
                ServiceLocation = location
            };
            dbContext.Tanks.Add(tank);
        }
        else
        {
            tank.Label = selection.TankLabel;
            tank.CapacityPounds = selection.CapacityPounds;
            tank.IsActive = true;
        }
        return tank;
    }

    private static CommissioningSessionView ToView(CommissioningSession session)
    {
        var location = session.Tank.ServiceLocation;
        return new(
            session.Id,
            session.DeviceId,
            session.Device.SerialNumber,
            session.Status,
            session.CreatedAtUtc,
            session.ExpiresAtUtc,
            session.Dealer.DisplayName,
            location.CustomerAccount.DisplayName,
            location.DisplayName,
            location.AddressSummary ?? string.Empty,
            session.Tank.Label,
            MillimetersToCentimeters(session.TankDepthMm),
            session.ActivatedAtUtc,
            session.CompletedAtUtc,
            session.FailureCode);
    }

    private static ProvisioningAuditEvent CreateAudit(
        CommissioningSession session,
        string eventType,
        string actorType,
        string actorId,
        DateTimeOffset occurredAt,
        object details) =>
        new()
        {
            DeviceId = session.DeviceId,
            CommissioningSessionId = session.Id,
            EventType = eventType,
            ActorType = actorType,
            ActorId = actorId,
            DetailsJson = JsonSerializer.Serialize(details),
            OccurredAtUtc = occurredAt
        };

    private static bool IsValidTechnician(StaffActor technician) =>
        technician.Role == StaffRole.DealerTechnician
        && !string.IsNullOrWhiteSpace(technician.DealerExternalId)
        && !string.IsNullOrWhiteSpace(technician.DealerName);

    private static CreateCommissioningSessionRequest Normalize(CreateCommissioningSessionRequest request) =>
        request with
        {
            WaterFlexCustomerId = request.WaterFlexCustomerId.Trim(),
            WaterFlexLocationId = request.WaterFlexLocationId.Trim(),
            WaterFlexAssetId = request.WaterFlexAssetId.Trim(),
            SerialNumber = request.SerialNumber.Trim().ToUpperInvariant(),
            WaterFlexWorkOrderId = string.IsNullOrWhiteSpace(request.WaterFlexWorkOrderId)
                ? null
                : request.WaterFlexWorkOrderId.Trim()
        };

    private static IReadOnlyList<ProvisioningValidationError> Validate(
        CreateCommissioningSessionRequest request)
    {
        var errors = new List<ProvisioningValidationError>();
        Require(request.WaterFlexCustomerId, nameof(request.WaterFlexCustomerId), 128, errors);
        Require(request.WaterFlexLocationId, nameof(request.WaterFlexLocationId), 128, errors);
        Require(request.WaterFlexAssetId, nameof(request.WaterFlexAssetId), 128, errors);
        Require(request.SerialNumber, nameof(request.SerialNumber), 64, errors);

        if (request.SerialNumber.Length < 4
            || request.SerialNumber.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            errors.Add(new(nameof(request.SerialNumber),
                "Serial number must contain 4-64 letters, numbers, or hyphens."));
        }
        if (request.WaterFlexWorkOrderId?.Length > 128)
        {
            errors.Add(new(nameof(request.WaterFlexWorkOrderId),
                "Work order ID cannot exceed 128 characters."));
        }
        if (request.TankDepthCm is < 10m or > 450m)
        {
            errors.Add(new(nameof(request.TankDepthCm),
                "Tank depth must be between 10 and 450 centimeters."));
        }
        if (decimal.Truncate(request.TankDepthCm * 10m) != request.TankDepthCm * 10m)
        {
            errors.Add(new(nameof(request.TankDepthCm),
                "Tank depth supports one decimal place in centimeters."));
        }
        return errors;
    }

    private static void Require(
        string value,
        string field,
        int maximumLength,
        ICollection<ProvisioningValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
        {
            errors.Add(new(field, $"{field} is required and cannot exceed {maximumLength} characters."));
        }
    }

    private static int CentimetersToMillimeters(decimal centimeters) =>
        decimal.ToInt32(decimal.Round(centimeters * 10m, 0, MidpointRounding.AwayFromZero));

    private static decimal MillimetersToCentimeters(int millimeters) => millimeters / 10m;

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: "23505" };
}
