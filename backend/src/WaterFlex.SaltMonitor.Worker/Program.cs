using WaterFlex.SaltMonitor.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<DeliveryOutboxWorker>();

var host = builder.Build();
host.Run();
