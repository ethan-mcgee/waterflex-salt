using System.Data;
using Microsoft.EntityFrameworkCore;
using WaterFlex.SaltMonitor.Domain.Security;
using WaterFlex.SaltMonitor.Provisioning;

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence;

public sealed class EfInstallationWorkOrderDirectory(SaltMonitorDbContext dbContext)
    : IInstallationWorkOrderDirectory
{
    public async Task<InstallationWorkOrder?> FindEligibleAsync(
        string workOrderNumber,
        string dealerExternalId,
        CancellationToken cancellationToken = default)
    {
        var number = workOrderNumber.Trim().ToUpperInvariant();
        var order = await dbContext.InstallationWorkOrders
            .AsNoTracking()
            .Include(candidate => candidate.Dealer)
            .Include(candidate => candidate.CustomerAccount)
            .Include(candidate => candidate.ServiceLocation)
            .Include(candidate => candidate.Tank)
            .SingleOrDefaultAsync(candidate =>
                candidate.WorkOrderNumber == number
                && candidate.Dealer.ExternalId == dealerExternalId
                && candidate.Status == WorkOrderStatus.Open,
                cancellationToken);
        return order is null ? null : ToDirectoryOrder(order);
    }

    private static InstallationWorkOrder ToDirectoryOrder(InstallationWorkOrderRecord order) =>
        new(
            order.WorkOrderNumber,
            order.Dealer.ExternalId,
            order.CustomerAccount.WaterFlexCustomerId,
            order.ServiceLocation.WaterFlexLocationId,
            order.Tank.WaterFlexAssetId!,
            order.CustomerAccount.DisplayName,
            order.ServiceLocation.DisplayName,
            order.ServiceLocation.AddressSummary!,
            order.Tank.Label)
        { Id = order.Id };
}

public sealed class EfInstallationWorkOrderService(
    SaltMonitorDbContext dbContext,
    TimeProvider timeProvider) : IInstallationWorkOrderService
{
    private static readonly CommissioningSessionStatus[] LiveSessionStatuses =
    [
        CommissioningSessionStatus.PendingSensor,
        CommissioningSessionStatus.ActivatedAwaitingHealth,
        CommissioningSessionStatus.AwaitingFirstTelemetry
    ];

    public async Task<InstallationWorkOrderResult> CreateAsync(
        CreateInstallationWorkOrderRequest request,
        StaffActor administrator,
        CancellationToken cancellationToken = default)
    {
        if (!IsDealerAdministrator(administrator))
        {
            return InstallationWorkOrderResult.Failed(InstallationWorkOrderFailure.InvalidAdministrator);
        }

        var customerName = request.CustomerName?.Trim() ?? string.Empty;
        var locationName = request.LocationName?.Trim() ?? string.Empty;
        var address = request.Address?.Trim() ?? string.Empty;
        var tankLocation = request.TankLocation?.Trim() ?? string.Empty;
        var errors = new List<ProvisioningValidationError>();
        AddRequiredError(errors, nameof(request.CustomerName), customerName);
        AddRequiredError(errors, nameof(request.LocationName), locationName);
        AddRequiredError(errors, nameof(request.Address), address);
        AddRequiredError(errors, nameof(request.TankLocation), tankLocation);
        if (errors.Count > 0)
        {
            return InstallationWorkOrderResult.Failed(InstallationWorkOrderFailure.InvalidRequest, errors);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var dealer = await dbContext.Dealers.SingleOrDefaultAsync(
            candidate => candidate.ExternalId == administrator.DealerExternalId && candidate.IsActive,
            cancellationToken);
        if (dealer is null)
        {
            return InstallationWorkOrderResult.Failed(InstallationWorkOrderFailure.InvalidAdministrator);
        }

        var sequence = await dbContext.Database
            .SqlQuery<long>($"SELECT nextval('\"InstallationWorkOrderNumberSequence\"') AS \"Value\"")
            .SingleAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var targetKey = Guid.NewGuid().ToString("N");
        var customer = new CustomerAccount
        {
            Id = Guid.NewGuid(), WaterFlexCustomerId = $"WO-C-{targetKey}", DisplayName = customerName,
            IsActive = true, LastSyncedAtUtc = now
        };
        var location = new ServiceLocation
        {
            Id = Guid.NewGuid(), CustomerAccountId = customer.Id, WaterFlexLocationId = $"WO-L-{targetKey}",
            DisplayName = locationName, AddressSummary = address, IsActive = true, LastSyncedAtUtc = now,
            CustomerAccount = customer
        };
        var tank = new Tank
        {
            Id = Guid.NewGuid(), ServiceLocationId = location.Id, WaterFlexAssetId = $"WO-A-{targetKey}",
            Label = tankLocation, IsActive = true, ServiceLocation = location
        };
        var order = new InstallationWorkOrderRecord
        {
            Id = Guid.NewGuid(), NumberSequence = sequence, WorkOrderNumber = $"WO-{sequence:D6}",
            DealerId = dealer.Id, CustomerAccountId = customer.Id, ServiceLocationId = location.Id, TankId = tank.Id,
            Status = WorkOrderStatus.Open, CreatedByActorId = administrator.UserId,
            CreatedByDisplayName = administrator.DisplayName, CreatedAtUtc = now,
            Dealer = dealer, CustomerAccount = customer, ServiceLocation = location, Tank = tank
        };
        dbContext.InstallationWorkOrders.Add(order);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return InstallationWorkOrderResult.Success(ToView(order));
    }

    public async Task<IReadOnlyList<InstallationWorkOrderManagementView>> ListAsync(
        StaffActor administrator,
        CancellationToken cancellationToken = default)
    {
        if (!IsDealerAdministrator(administrator)) return [];
        var orders = await dbContext.InstallationWorkOrders
            .AsNoTracking()
            .Include(order => order.CustomerAccount)
            .Include(order => order.ServiceLocation)
            .Include(order => order.Tank)
            .Where(order => order.Dealer.ExternalId == administrator.DealerExternalId)
            .OrderByDescending(order => order.CreatedAtUtc)
            .ToArrayAsync(cancellationToken);
        return orders.Select(ToView).ToArray();
    }

    public async Task<InstallationWorkOrderResult> CancelAsync(
        Guid id,
        CancelInstallationWorkOrderRequest request,
        StaffActor administrator,
        CancellationToken cancellationToken = default)
    {
        if (!IsDealerAdministrator(administrator))
            return InstallationWorkOrderResult.Failed(InstallationWorkOrderFailure.InvalidAdministrator);
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
            return InstallationWorkOrderResult.Failed(InstallationWorkOrderFailure.InvalidRequest,
                [new(nameof(request.Reason), "Cancellation reason is required.")]);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var dealerId = await dbContext.Dealers
            .Where(dealer => dealer.ExternalId == administrator.DealerExternalId)
            .Select(dealer => (Guid?)dealer.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (dealerId is null) return InstallationWorkOrderResult.Failed(InstallationWorkOrderFailure.NotFound);
        var lockedOrders = await dbContext.InstallationWorkOrders
            .FromSqlInterpolated($"SELECT *, xmin AS \"xmin\" FROM \"InstallationWorkOrders\" WHERE \"Id\" = {id} FOR UPDATE")
            .ToListAsync(cancellationToken);
        var order = lockedOrders.SingleOrDefault();
        if (order is null || order.DealerId != dealerId) return InstallationWorkOrderResult.Failed(InstallationWorkOrderFailure.NotFound);
        await dbContext.Entry(order).Reference(candidate => candidate.CustomerAccount).LoadAsync(cancellationToken);
        await dbContext.Entry(order).Reference(candidate => candidate.ServiceLocation).LoadAsync(cancellationToken);
        await dbContext.Entry(order).Reference(candidate => candidate.Tank).LoadAsync(cancellationToken);
        if (order.RowVersion != request.RowVersion || order.Status != WorkOrderStatus.Open)
            return InstallationWorkOrderResult.Failed(InstallationWorkOrderFailure.Conflict);
        var now = timeProvider.GetUtcNow();
        var hasLiveSession = await dbContext.CommissioningSessions.AnyAsync(session =>
            session.InstallationWorkOrderId == order.Id
            && LiveSessionStatuses.Contains(session.Status)
            && session.ExpiresAtUtc > now,
            cancellationToken);
        if (hasLiveSession) return InstallationWorkOrderResult.Failed(InstallationWorkOrderFailure.Conflict);

        order.Status = WorkOrderStatus.Cancelled;
        order.CancelledByActorId = administrator.UserId;
        order.CancelledByDisplayName = administrator.DisplayName;
        order.CancelledAtUtc = now;
        order.CancellationReason = reason;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return InstallationWorkOrderResult.Failed(InstallationWorkOrderFailure.Conflict);
        }
        return InstallationWorkOrderResult.Success(ToView(order));
    }

    private static bool IsDealerAdministrator(StaffActor actor) =>
        actor.Role == StaffRole.DealerAdministrator && !string.IsNullOrWhiteSpace(actor.DealerExternalId);

    private static void AddRequiredError(List<ProvisioningValidationError> errors, string field, string value)
    {
        if (value.Length == 0) errors.Add(new(field, $"{field} is required."));
    }

    private static InstallationWorkOrderManagementView ToView(InstallationWorkOrderRecord order) => new(
        order.Id, order.WorkOrderNumber, order.Status, order.CustomerAccount.DisplayName,
        order.ServiceLocation.DisplayName, order.ServiceLocation.AddressSummary!, order.Tank.Label,
        order.CreatedByActorId, order.CreatedByDisplayName, order.CreatedAtUtc, order.CompletedAtUtc,
        order.CancelledByActorId, order.CancelledByDisplayName, order.CancelledAtUtc, order.CancellationReason, order.RowVersion);
}
