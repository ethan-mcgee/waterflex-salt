using WaterFlex.SaltMonitor.Worker;
using WaterFlex.SaltMonitor.Infrastructure.Persistence;
using Amazon.CognitoIdentityProvider;
using Amazon.SecretsManager;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSaltMonitorPersistence();
builder.Services.Configure<StaffProvisioningOptions>(builder.Configuration.GetSection(StaffProvisioningOptions.SectionName));
builder.Services.AddSingleton<IAmazonCognitoIdentityProvider>(_ => new AmazonCognitoIdentityProviderClient());
builder.Services.AddSingleton<IAmazonSecretsManager>(_ => new AmazonSecretsManagerClient());
builder.Services.AddHttpClient<CloudflareStaffAccessGateway>();
builder.Services.AddScoped<StaffProvisioningProcessor>();
builder.Services.AddHostedService<AlertEvaluationWorker>();
builder.Services.AddHostedService<DeliveryTicketOutboxWorker>();
builder.Services.AddHostedService<TelemetryHistoryWorker>();
builder.Services.AddHostedService<StaffProvisioningWorker>();

var host = builder.Build();
host.Run();
