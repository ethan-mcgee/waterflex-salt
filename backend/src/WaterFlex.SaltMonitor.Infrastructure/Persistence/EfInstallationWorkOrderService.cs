using System.Data;
using System.Text.RegularExpressions;
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
            order.ServiceLocation.AddressSummary ?? string.Empty,
            order.Tank.Label)
        { Id = order.Id };
}

public sealed class EfInstallationWorkOrderService(
    SaltMonitorDbContext dbContext,
    TimeProvider timeProvider) : IInstallationWorkOrderService
{
    private static readonly HashSet<string> UsStateCodes = new(StringComparer.Ordinal)
    {
        "AL", "AK", "AZ", "AR", "CA", "CO", "CT", "DE", "FL", "GA",
        "HI", "ID", "IL", "IN", "IA", "KS", "KY", "LA", "ME", "MD",
        "MA", "MI", "MN", "MS", "MO", "MT", "NE", "NV", "NH", "NJ",
        "NM", "NY", "NC", "ND", "OH", "OK", "OR", "PA", "RI", "SC",
        "SD", "TN", "TX", "UT", "VT", "VA", "WA", "WV", "WI", "WY", "DC"
    };
    private static readonly Regex ZipCodePattern = new("^[0-9]{5}(-[0-9]{4})?$", RegexOptions.CultureInvariant);
    private static readonly CommissioningSessionStatus[] LiveSessionStatuses =
    [
        CommissioningSessionStatus.PendingSensor,
        CommissioningSessionStatus.ActivatedAwaitingHealth,
        CommissioningSessionStatus.AwaitingFirstTelemetry
    ];

    public async Task<InstallationWorkOrderResult> CreateAsync(
        CreateInstallationWorkOrderRequest request,
        StaffActor administrator,
        string? requestedDealerExternalId = null,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveDealerAsync(administrator, requestedDealerExternalId, cancellationToken);
        if (!scope.IsSuccess) return InstallationWorkOrderResult.Failed(scope.Failure, scope.ValidationErrors);

        var firstName = request.FirstName?.Trim() ?? string.Empty;
        var lastName = request.LastName?.Trim() ?? string.Empty;
        var locationName = NullIfWhiteSpace(request.LocationName);
        var streetAddress = request.StreetAddress?.Trim() ?? string.Empty;
        var addressLine2 = NullIfWhiteSpace(request.AddressLine2);
        var city = request.City?.Trim() ?? string.Empty;
        var state = request.State?.Trim().ToUpperInvariant() ?? string.Empty;
        var zipCode = request.ZipCode?.Trim() ?? string.Empty;
        var errors = new List<ProvisioningValidationError>();
        ValidateRequired(errors, nameof(request.FirstName), firstName, 100);
        ValidateRequired(errors, nameof(request.LastName), lastName, 100);
        ValidateRequired(errors, nameof(request.StreetAddress), streetAddress, 200);
        ValidateRequired(errors, nameof(request.City), city, 100);
        ValidateRequired(errors, nameof(request.State), state, 2);
        ValidateRequired(errors, nameof(request.ZipCode), zipCode, 10);
        ValidateOptional(errors, nameof(request.LocationName), locationName, 200);
        ValidateOptional(errors, nameof(request.AddressLine2), addressLine2, 100);
        var customerName = $"{firstName} {lastName}";
        if (customerName.Length > 200)
            errors.Add(new(nameof(request.LastName), "Combined customer name cannot exceed 200 characters."));
        if (state.Length > 0 && !UsStateCodes.Contains(state))
            errors.Add(new(nameof(request.State), "State must be a two-letter code for a US state or DC."));
        if (zipCode.Length > 0 && !ZipCodePattern.IsMatch(zipCode))
            errors.Add(new(nameof(request.ZipCode), "ZIP code must contain 5 digits or use ZIP+4 format."));
        if (errors.Count > 0)
        {
            return InstallationWorkOrderResult.Failed(InstallationWorkOrderFailure.InvalidRequest, errors);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var dealer = scope.Dealer!;

        var sequence = await dbContext.Database
            .SqlQuery<long>($"SELECT nextval('\"InstallationWorkOrderNumberSequence\"') AS \"Value\"")
            .SingleAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var targetKey = Guid.NewGuid().ToString("N");
        var address = ComposeAddress(streetAddress, addressLine2, city, state, zipCode);
        var customer = new CustomerAccount
        {
            Id = Guid.NewGuid(), WaterFlexCustomerId = $"WO-C-{targetKey}", DisplayName = customerName,
            FirstName = firstName, LastName = lastName,
            IsActive = true, LastSyncedAtUtc = now
        };
        var location = new ServiceLocation
        {
            Id = Guid.NewGuid(), CustomerAccountId = customer.Id, WaterFlexLocationId = $"WO-L-{targetKey}",
            DisplayName = locationName, AddressSummary = address, StreetAddress = streetAddress,
            AddressLine2 = addressLine2, City = city, State = state, ZipCode = zipCode,
            IsActive = true, LastSyncedAtUtc = now,
            CustomerAccount = customer
        };
        var tank = new Tank
        {
            Id = Guid.NewGuid(), ServiceLocationId = location.Id, WaterFlexAssetId = $"WO-A-{targetKey}",
            Label = null, IsActive = true, ServiceLocation = location
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

    public async Task<InstallationWorkOrderListResult> ListAsync(
        StaffActor administrator,
        string? requestedDealerExternalId = null,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveDealerAsync(administrator, requestedDealerExternalId, cancellationToken);
        if (!scope.IsSuccess) return InstallationWorkOrderListResult.Failed(scope.Failure, scope.ValidationErrors);
        var orders = await dbContext.InstallationWorkOrders
            .AsNoTracking()
            .Include(order => order.CustomerAccount)
            .Include(order => order.ServiceLocation)
            .Include(order => order.Tank)
            .Where(order => order.DealerId == scope.Dealer!.Id)
            .OrderByDescending(order => order.CreatedAtUtc)
            .ToArrayAsync(cancellationToken);
        return InstallationWorkOrderListResult.Success(orders.Select(ToView).ToArray());
    }

    public async Task<InstallationWorkOrderResult> CancelAsync(
        Guid id,
        CancelInstallationWorkOrderRequest request,
        StaffActor administrator,
        string? requestedDealerExternalId = null,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveDealerAsync(administrator, requestedDealerExternalId, cancellationToken);
        if (!scope.IsSuccess) return InstallationWorkOrderResult.Failed(scope.Failure, scope.ValidationErrors);

        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
            return InstallationWorkOrderResult.Failed(InstallationWorkOrderFailure.InvalidRequest,
                [new(nameof(request.Reason), "Cancellation reason is required.")]);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var lockedOrders = await dbContext.InstallationWorkOrders
            .FromSqlInterpolated($"SELECT *, xmin AS \"xmin\" FROM \"InstallationWorkOrders\" WHERE \"Id\" = {id} FOR UPDATE")
            .ToListAsync(cancellationToken);
        var order = lockedOrders.SingleOrDefault();
        if (order is null || order.DealerId != scope.Dealer!.Id) return InstallationWorkOrderResult.Failed(InstallationWorkOrderFailure.NotFound);
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

    private async Task<DealerScopeResolution> ResolveDealerAsync(
        StaffActor actor,
        string? requestedDealerExternalId,
        CancellationToken cancellationToken)
    {
        string dealerExternalId;
        if (actor.Role == StaffRole.DealerAdministrator && !string.IsNullOrWhiteSpace(actor.DealerExternalId))
        {
            dealerExternalId = actor.DealerExternalId;
        }
        else if (actor.Role == StaffRole.WaterFlexAdministrator)
        {
            dealerExternalId = requestedDealerExternalId?.Trim() ?? string.Empty;
            if (dealerExternalId.Length == 0)
            {
                return DealerScopeResolution.Failed(
                    InstallationWorkOrderFailure.InvalidRequest,
                    [new("dealerExternalId", "An active dealer must be selected.")]);
            }
        }
        else
        {
            return DealerScopeResolution.Failed(InstallationWorkOrderFailure.InvalidAdministrator);
        }

        var dealer = await dbContext.Dealers.SingleOrDefaultAsync(
            candidate => candidate.ExternalId == dealerExternalId && candidate.IsActive,
            cancellationToken);
        return dealer is null
            ? DealerScopeResolution.Failed(InstallationWorkOrderFailure.NotFound)
            : DealerScopeResolution.Success(dealer);
    }

    private sealed record DealerScopeResolution(
        Dealer? Dealer,
        InstallationWorkOrderFailure Failure,
        IReadOnlyList<ProvisioningValidationError> ValidationErrors)
    {
        public bool IsSuccess => Failure == InstallationWorkOrderFailure.None;
        public static DealerScopeResolution Success(Dealer dealer) =>
            new(dealer, InstallationWorkOrderFailure.None, []);
        public static DealerScopeResolution Failed(
            InstallationWorkOrderFailure failure,
            IReadOnlyList<ProvisioningValidationError>? errors = null) => new(null, failure, errors ?? []);
    }

    private static void ValidateRequired(List<ProvisioningValidationError> errors, string field, string value, int maximumLength)
    {
        if (value.Length == 0) errors.Add(new(field, $"{field} is required."));
        else if (value.Length > maximumLength) errors.Add(new(field, $"{field} cannot exceed {maximumLength} characters."));
    }

    private static void ValidateOptional(List<ProvisioningValidationError> errors, string field, string? value, int maximumLength)
    {
        if (value?.Length > maximumLength) errors.Add(new(field, $"{field} cannot exceed {maximumLength} characters."));
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ComposeAddress(string streetAddress, string? addressLine2, string city, string state, string zipCode) =>
        string.Join(", ", new[] { streetAddress, addressLine2, city, $"{state} {zipCode}" }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static InstallationWorkOrderManagementView ToView(InstallationWorkOrderRecord order) => new(
        order.Id, order.WorkOrderNumber, order.Status, order.CustomerAccount.DisplayName,
        order.ServiceLocation.DisplayName, order.ServiceLocation.AddressSummary ?? string.Empty, order.Tank.Label,
        order.CustomerAccount.FirstName, order.CustomerAccount.LastName,
        order.ServiceLocation.StreetAddress, order.ServiceLocation.AddressLine2,
        order.ServiceLocation.City, order.ServiceLocation.State, order.ServiceLocation.ZipCode,
        order.CreatedByActorId, order.CreatedByDisplayName, order.CreatedAtUtc, order.CompletedAtUtc,
        order.CancelledByActorId, order.CancelledByDisplayName, order.CancelledAtUtc, order.CancellationReason, order.RowVersion);
}
