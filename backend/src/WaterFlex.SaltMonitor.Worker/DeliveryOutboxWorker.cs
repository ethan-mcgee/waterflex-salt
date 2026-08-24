using WaterFlex.SaltMonitor.Operations;

namespace WaterFlex.SaltMonitor.Worker;

/// <summary>Processes persisted delivery-ticket outbox records independently of telemetry requests.</summary>
public sealed class DeliveryOutboxWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<DeliveryOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IAlertWorkProcessor>();
                var processed = await processor.ProcessNextAsync(stoppingToken);
                if (!processed) await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Alert evaluation worker iteration failed");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }
}
