using WaterFlex.SaltMonitor.Worker;
using WaterFlex.SaltMonitor.Infrastructure.Persistence;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSaltMonitorPersistence();
builder.Services.AddHostedService<DeliveryOutboxWorker>();
builder.Services.AddHostedService<TelemetryHistoryWorker>();

var host = builder.Build();
host.Run();
