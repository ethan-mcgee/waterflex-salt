using WaterFlex.SaltMonitor.Domain.Abstractions;
using WaterFlex.SaltMonitor.Domain.Model;

namespace WaterFlex.SaltMonitor.Infrastructure;

/// <summary>
/// Placeholder gateway that returns a well-formed result until the real WaterFlex/RouteFlex
/// delivery-ticket endpoints are supplied. Swap this for the HTTP implementation later.
/// </summary>
public sealed class StubDeliveryTicketGateway : IDeliveryTicketGateway
{
    public Task<DeliveryTicketResult> CreateDeliveryTicketAsync(
        DeliveryTicketRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = new DeliveryTicketResult(
            ExternalTicketId: $"STUB-{request.IdempotencyKey}",
            Status: "Created",
            CreatedAt: DateTimeOffset.UtcNow);

        return Task.FromResult(result);
    }
}
