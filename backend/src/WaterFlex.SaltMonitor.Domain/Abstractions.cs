using WaterFlex.SaltMonitor.Domain.Model;

namespace WaterFlex.SaltMonitor.Domain.Abstractions;

/// <summary>
/// Creates delivery tickets in WaterFlex/RouteFlex. Endpoints are deferred, so a stub
/// implementation is used until the real contract is supplied.
/// </summary>
public interface IDeliveryTicketGateway
{
    Task<DeliveryTicketResult> CreateDeliveryTicketAsync(
        DeliveryTicketRequest request,
        CancellationToken cancellationToken = default);
}
