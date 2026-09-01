using Microsoft.Extensions.Options;
using WaterFlex.SaltMonitor.Infrastructure.Persistence;

namespace WaterFlex.SaltMonitor.Worker;

/// <summary>Periodically runs telemetry history maintenance (bucket aggregation/rollup) on a fixed interval, skipping a run if another worker instance already holds the maintenance lock.</summary>
public sealed class TelemetryHistoryWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<TelemetryHistoryOptions> options,
    ILogger<TelemetryHistoryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(options.Value.MaintenanceIntervalMinutes);
        while (!stoppingToken.IsCancellationRequested)
        {
            var startedAt = DateTimeOffset.UtcNow;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var maintenance = scope.ServiceProvider
                    .GetRequiredService<ITelemetryHistoryMaintenanceService>();
                var result = await maintenance.RunAsync(stoppingToken);
                if (result.SkippedBecauseAlreadyRunning)
                {
                    logger.LogInformation("Telemetry history maintenance skipped because another worker holds the lock");
                }
                else
                {
                    logger.LogInformation(
                        "Telemetry history maintenance duration was {DurationMs} ms",
                        (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Telemetry history worker iteration failed");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
