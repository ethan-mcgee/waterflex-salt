using Microsoft.Extensions.Options;
using WaterFlex.SaltMonitor.Infrastructure.Persistence;

namespace WaterFlex.SaltMonitor.Worker;

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
