namespace WaterFlex.SaltMonitor.Worker;

/// <summary>Processes persisted delivery-ticket outbox records independently of telemetry requests.</summary>
public sealed class DeliveryOutboxWorker(ILogger<DeliveryOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // TODO(plan-c C6): lease and drain the transactional outbox through
        // IDeliveryTicketGateway with bounded retries and dead-letter state.
        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Delivery outbox worker heartbeat at {Time}", DateTimeOffset.UtcNow);
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}