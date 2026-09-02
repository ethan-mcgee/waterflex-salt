using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WaterFlex.SaltMonitor.Domain.Security;
using WaterFlex.SaltMonitor.Domain.Monitoring;
using WaterFlex.SaltMonitor.Infrastructure.Persistence;
using WaterFlex.SaltMonitor.Ingestion;
using WaterFlex.SaltMonitor.Provisioning;
using Xunit;

namespace WaterFlex.SaltMonitor.Tests;

public sealed class BootstrapProvisioningServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 24, 16, 0, 0, TimeSpan.Zero);
    private static readonly StaffActor NorthStarTechnician = new(
        "north-star-jordan",
        "Jordan Lee",
        StaffRole.DealerTechnician,
        "WF-D-NORTH-STAR",
        "North Star Water Systems");
    private static readonly StaffActor LakesTechnician = new(
        "lakes-water-sam",
        "Sam Rivera",
        StaffRole.DealerTechnician,
        "WF-D-LAKES-WATER",
        "Lakes Water Conditioning");
    private static readonly StaffActor FactoryWorker = new(
        "wf-factory-riley",
        "Riley Chen",
        StaffRole.FactoryWorker,
        null,
        null);
    private static readonly StaffActor OtherFactoryWorker = new(
        "wf-factory-casey",
        "Casey Morgan",
        StaffRole.FactoryWorker,
        null,
        null);
    private static readonly StaffActor WaterFlexAdministrator = new(
        "wf-admin-avery",
        "Avery Patel",
        StaffRole.WaterFlexAdministrator,
        null,
        null);

    [Fact]
    public async Task FactoryRegistration_StoresRegisteredInventoryAndHashOnly()
    {
        await using var database = await TestDatabase.CreateAsync();
        var timeProvider = new MutableTimeProvider(Now);
        var service = new EfFactoryDeviceRegistrationService(database.Context, timeProvider);
        var secretHash = SHA256.HashData(Encoding.UTF8.GetBytes("factory-secret"));

        var result = await service.RegisterAsync(
            CreateFactoryRequest(secretHash),
            FactoryWorker);

        Assert.True(result.IsSuccess);
        var device = await database.Context.Devices.SingleAsync();
        var credential = await database.Context.DeviceBootstrapCredentials.SingleAsync();
        var auditEvent = await database.Context.ProvisioningAuditEvents.SingleAsync();
        Assert.Equal(DeviceLifecycleStatus.Registered, device.Status);
        Assert.Null(device.CommissionedAtUtc);
        Assert.Equal(secretHash, credential.SecretHash);
        Assert.DoesNotContain("factory-secret", auditEvent.DetailsJson, StringComparison.Ordinal);
        Assert.Equal("factory_device_registered", auditEvent.EventType);
    }

    [Fact]
    public async Task FactoryJob_IsVisibleOnlyToCreatorOrWaterFlexAdministrator()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new EfFactoryDeviceRegistrationService(database.Context, new MutableTimeProvider(Now));
        var created = await service.RegisterAsync(
            CreateFactoryRequest(SHA256.HashData(Encoding.UTF8.GetBytes("factory-secret"))),
            FactoryWorker);

        var hidden = await service.FindByIdempotencyKeyAsync("factory-test-job-0001", OtherFactoryWorker);
        var administrator = await service.FindByIdempotencyKeyAsync("factory-test-job-0001", WaterFlexAdministrator);

        Assert.True(created.IsSuccess);
        Assert.False(hidden.IsSuccess);
        Assert.True(administrator.IsSuccess);
    }

    [Fact]
    public async Task FactoryActiveJob_ResumesCreatorsOwnNonTerminalJobOnly()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new EfFactoryDeviceRegistrationService(database.Context, new MutableTimeProvider(Now));
        var created = await service.RegisterAsync(
            CreateFactoryRequest(SHA256.HashData(Encoding.UTF8.GetBytes("factory-secret"))),
            FactoryWorker);
        Assert.True(created.IsSuccess);

        var resumedByCreator = await service.FindActiveByOperatorAsync(FactoryWorker);
        var resumedByOther = await service.FindActiveByOperatorAsync(OtherFactoryWorker);

        Assert.True(resumedByCreator.IsSuccess);
        Assert.Equal(created.Registration!.DeviceId, resumedByCreator.Registration!.DeviceId);
        Assert.Equal("factory-test-job-0001", resumedByCreator.Registration.IdempotencyKey);
        Assert.False(resumedByOther.IsSuccess);
    }

    [Fact]
    public async Task FactoryActiveJob_ExcludesProvisionedJobsSoANewOneCanStart()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new EfFactoryDeviceRegistrationService(database.Context, new MutableTimeProvider(Now));
        var created = await service.RegisterAsync(
            CreateFactoryRequest(SHA256.HashData(Encoding.UTF8.GetBytes("factory-secret"))),
            FactoryWorker);
        await service.RecordVerificationAsync(
            created.Registration!.DeviceId,
            new(true, true, true, true, "1.0.0", null),
            FactoryWorker);

        var resumed = await service.FindActiveByOperatorAsync(FactoryWorker);

        Assert.False(resumed.IsSuccess);
    }

    [Fact]
    public async Task FactoryActiveJob_ResumesAQuarantinedJobUntilRetried()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new EfFactoryDeviceRegistrationService(database.Context, new MutableTimeProvider(Now));
        var created = await service.RegisterAsync(
            CreateFactoryRequest(SHA256.HashData(Encoding.UTF8.GetBytes("factory-secret"))),
            FactoryWorker);
        await service.RecordVerificationAsync(
            created.Registration!.DeviceId,
            new(false, true, true, true, "1.0.0", "firmware_check_failed"),
            FactoryWorker);

        var resumed = await service.FindActiveByOperatorAsync(FactoryWorker);

        Assert.True(resumed.IsSuccess);
        Assert.Equal(FactoryProvisioningStatus.Quarantined, resumed.Registration!.Status);
        Assert.Equal(created.Registration.SerialNumber, resumed.Registration.SerialNumber);
    }

    [Fact]
    public async Task FlashAuthorization_RegistrationMintsATokenThatVerifiesOnceAndRejectsReplay()
    {
        await using var database = await TestDatabase.CreateAsync();
        var timeProvider = new MutableTimeProvider(Now);
        var registrationService = new EfFactoryDeviceRegistrationService(database.Context, timeProvider);
        var flashAuthorizationService = new EfFactoryFlashAuthorizationService(database.Context, timeProvider);
        var created = await registrationService.RegisterAsync(
            CreateFactoryRequest(SHA256.HashData(Encoding.UTF8.GetBytes("factory-secret"))),
            FactoryWorker);
        var deviceId = created.Registration!.DeviceId;
        var token = created.Registration.FlashAuthorizationToken!;

        var wrongDevice = await flashAuthorizationService.VerifyAsync(Guid.NewGuid(), token);
        var firstUse = await flashAuthorizationService.VerifyAsync(deviceId, token);
        var replay = await flashAuthorizationService.VerifyAsync(deviceId, token);

        Assert.NotNull(token);
        Assert.False(wrongDevice);
        Assert.True(firstUse);
        Assert.False(replay);
    }

    [Fact]
    public async Task FlashAuthorization_ReissuingRevokesThePriorUnconsumedToken()
    {
        await using var database = await TestDatabase.CreateAsync();
        var timeProvider = new MutableTimeProvider(Now);
        var registrationService = new EfFactoryDeviceRegistrationService(database.Context, timeProvider);
        var flashAuthorizationService = new EfFactoryFlashAuthorizationService(database.Context, timeProvider);
        var created = await registrationService.RegisterAsync(
            CreateFactoryRequest(SHA256.HashData(Encoding.UTF8.GetBytes("factory-secret"))),
            FactoryWorker);
        var deviceId = created.Registration!.DeviceId;
        var staleToken = created.Registration.FlashAuthorizationToken!;

        var resumed = await registrationService.FindActiveByOperatorAsync(FactoryWorker);
        var freshToken = resumed.Registration!.FlashAuthorizationToken!;

        var staleUse = await flashAuthorizationService.VerifyAsync(deviceId, staleToken);
        var freshUse = await flashAuthorizationService.VerifyAsync(deviceId, freshToken);

        Assert.NotEqual(staleToken, freshToken);
        Assert.False(staleUse);
        Assert.True(freshUse);
    }

    [Fact]
    public async Task FlashAuthorization_QuarantineRevokesAnyLiveTokenSoAFlashCannotProceed()
    {
        await using var database = await TestDatabase.CreateAsync();
        var timeProvider = new MutableTimeProvider(Now);
        var registrationService = new EfFactoryDeviceRegistrationService(database.Context, timeProvider);
        var flashAuthorizationService = new EfFactoryFlashAuthorizationService(database.Context, timeProvider);
        var created = await registrationService.RegisterAsync(
            CreateFactoryRequest(SHA256.HashData(Encoding.UTF8.GetBytes("factory-secret"))),
            FactoryWorker);
        var deviceId = created.Registration!.DeviceId;
        var token = created.Registration.FlashAuthorizationToken!;

        await registrationService.RecordVerificationAsync(
            deviceId,
            new(false, true, true, true, "1.0.0", "firmware_check_failed"),
            FactoryWorker);

        var authorized = await flashAuthorizationService.VerifyAsync(deviceId, token);

        Assert.False(authorized);
    }

    [Fact]
    public async Task FactoryVerification_QuarantinesFailureAndMakesProvisionedTerminal()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new EfFactoryDeviceRegistrationService(database.Context, new MutableTimeProvider(Now));
        var created = await service.RegisterAsync(
            CreateFactoryRequest(SHA256.HashData(Encoding.UTF8.GetBytes("factory-secret"))),
            FactoryWorker);
        var deviceId = created.Registration!.DeviceId;

        var failed = await service.RecordVerificationAsync(
            deviceId,
            new(false, true, true, true, "1.0.0", "firmware_check_failed"),
            FactoryWorker);
        var retried = await service.RetryAsync(deviceId, FactoryWorker);
        var passed = await service.RecordVerificationAsync(
            deviceId,
            new(true, true, true, true, "1.0.0", null),
            FactoryWorker);

        Assert.Equal(FactoryProvisioningStatus.Quarantined, failed.Status);
        Assert.Equal(FactoryProvisioningStatus.Registered, retried.Status);
        Assert.Equal(FactoryProvisioningStatus.Provisioned, passed.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RetryAsync(deviceId, FactoryWorker));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordVerificationAsync(
            deviceId,
            new(true, true, true, true, "1.0.0", null),
            FactoryWorker));
    }

    [Fact]
    public async Task SessionCreation_ReservesRegisteredDeviceWithoutInstallationOrOperationalToken()
    {
        await using var database = await TestDatabase.CreateAsync();
        var timeProvider = new MutableTimeProvider(Now);
        await RegisterFactoryDeviceAsync(database.Context, timeProvider);
        var service = CreateSessionService(database.Context, timeProvider);

        var result = await service.CreateAsync(CreateSessionRequest(), NorthStarTechnician);

        Assert.True(result.IsSuccess);
        Assert.Equal(CommissioningSessionStatus.PendingSensor, result.Session!.Status);
        Assert.Equal(Now.AddMinutes(30), result.Session.ExpiresAtUtc);
        Assert.Equal(0, await database.Context.DeviceInstallations.CountAsync());
        Assert.Equal(0, await database.Context.DeviceCredentials.CountAsync());
        Assert.Equal(
            DeviceLifecycleStatus.Commissioning,
            (await database.Context.Devices.SingleAsync()).Status);
        Assert.Equal(1, await database.Context.CommissioningSessions.CountAsync());
    }

    [Fact]
    public async Task SessionLookup_IsDealerScopedAndCancellationReleasesDevice()
    {
        await using var database = await TestDatabase.CreateAsync();
        var timeProvider = new MutableTimeProvider(Now);
        await RegisterFactoryDeviceAsync(database.Context, timeProvider);
        var service = CreateSessionService(database.Context, timeProvider);
        var created = await service.CreateAsync(CreateSessionRequest(), NorthStarTechnician);

        var otherDealer = await service.GetAsync(created.Session!.SessionId, LakesTechnician);
        var cancelled = await service.CancelAsync(created.Session.SessionId, NorthStarTechnician);

        Assert.Equal(CommissioningSessionFailure.SessionNotFound, otherDealer.Failure);
        Assert.True(cancelled.IsSuccess);
        Assert.Equal(CommissioningSessionStatus.Cancelled, cancelled.Session!.Status);
        Assert.Equal(
            DeviceLifecycleStatus.Registered,
            (await database.Context.Devices.SingleAsync()).Status);
    }

    [Fact]
    public async Task SessionLookup_ExpiresPendingReservationAndReleasesDevice()
    {
        await using var database = await TestDatabase.CreateAsync();
        var timeProvider = new MutableTimeProvider(Now);
        await RegisterFactoryDeviceAsync(database.Context, timeProvider);
        var service = CreateSessionService(database.Context, timeProvider);
        var created = await service.CreateAsync(CreateSessionRequest(), NorthStarTechnician);
        timeProvider.Advance(TimeSpan.FromMinutes(31));

        var expired = await service.GetAsync(created.Session!.SessionId, NorthStarTechnician);

        Assert.True(expired.IsSuccess);
        Assert.Equal(CommissioningSessionStatus.Expired, expired.Session!.Status);
        Assert.Equal(
            DeviceLifecycleStatus.Registered,
            (await database.Context.Devices.SingleAsync()).Status);
    }

    [Fact]
    public async Task WorkOrderSession_RequiresTankLocationWhenOrderDoesNotProvideOne()
    {
        await using var database = await TestDatabase.CreateAsync();
        var timeProvider = new MutableTimeProvider(Now);
        await RegisterFactoryDeviceAsync(database.Context, timeProvider);
        var service = CreateSessionService(database.Context, timeProvider);

        var result = await service.CreateFromWorkOrderAsync(
            new("WO-82418", "WF-NANO-0001", null, 150m),
            NorthStarTechnician);

        Assert.Equal(CommissioningSessionFailure.TankLocationRequired, result.Failure);
        Assert.Contains(result.ValidationErrors, error => error.Field == nameof(CreateWorkOrderCommissioningSessionRequest.TankLocation));
    }

    [Fact]
    public async Task WorkOrderLookup_IsDealerScoped()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateSessionService(database.Context, new MutableTimeProvider(Now));

        var visible = await service.FindWorkOrderAsync("WO-82417", NorthStarTechnician);
        var hidden = await service.FindWorkOrderAsync("WO-82417", LakesTechnician);

        Assert.NotNull(visible);
        Assert.Equal("North Ridge Apartments", visible!.CustomerDisplayName);
        Assert.Null(hidden);
    }

    [Fact]
    public async Task Activation_RequiresHealthAndFirstTelemetryBeforeCompletion()
    {
        await using var database = await TestDatabase.CreateAsync();
        var timeProvider = new MutableTimeProvider(Now);
        var bootstrapSecret = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var registration = new EfFactoryDeviceRegistrationService(database.Context, timeProvider);
        var registered = await registration.RegisterAsync(
            CreateFactoryRequest(SHA256.HashData(bootstrapSecret)),
            FactoryWorker);
        Assert.True(registered.IsSuccess);

        var sessionService = CreateSessionService(database.Context, timeProvider);
        var reserved = await sessionService.CreateAsync(CreateSessionRequest(), NorthStarTechnician);
        var operationalSecret = Enumerable.Range(33, 32).Select(value => (byte)value).ToArray();
        var activation = new EfDeviceBootstrapActivationService(database.Context, timeProvider);
        var activated = await activation.ActivateAsync(
            $"wf_boot_test_0001.{Base64Url(bootstrapSecret)}",
            new(
                Guid.NewGuid(),
                "WF-NANO-0001",
                "1.0.0",
                "pilot-v1",
                "wf_dev_commissioning_test",
                Convert.ToBase64String(SHA256.HashData(operationalSecret))));

        Assert.True(activated.IsSuccess);
        var session = await database.Context.CommissioningSessions.SingleAsync();
        Assert.Equal(CommissioningSessionStatus.ActivatedAwaitingHealth, session.Status);
        Assert.Null(session.CompletedAtUtc);

        var schedule = new MonitoringSchedule(TimeSpan.FromMinutes(1));
        var health = new EfDeviceHealthService(database.Context, timeProvider, schedule);
        var healthResult = await health.ReportAsync(
            registered.Registration!.DeviceId,
            new(1, "1.0.0", Now, 1000, SensorHealthStatus.Healthy, null, -55, 0, true));
        Assert.True(healthResult.IsSuccess);
        Assert.Equal(
            CommissioningSessionStatus.AwaitingFirstTelemetry,
            (await database.Context.CommissioningSessions.SingleAsync()).Status);

        var telemetry = new EfTelemetryIngestionService(
            database.Context,
            new TelemetryBatchValidator(timeProvider),
            timeProvider,
            schedule);
        var telemetryResult = await telemetry.IngestAsync(
            registered.Registration.DeviceId,
            new(1, "1.0.0", [new(Guid.NewGuid(), 1, Now, 2000, 500, 95, 1, -55)]));

        Assert.True(telemetryResult.IsSuccess);
        session = await database.Context.CommissioningSessions.SingleAsync();
        Assert.Equal(CommissioningSessionStatus.Completed, session.Status);
        Assert.Equal(Now, session.CompletedAtUtc);
    }

    private static EfCommissioningSessionService CreateSessionService(
        SaltMonitorDbContext context,
        TimeProvider timeProvider) =>
        new(
            context,
            new DevelopmentWaterFlexCustomerDirectory(),
            new DevelopmentInstallationWorkOrderDirectory(),
            timeProvider);

    private static async Task RegisterFactoryDeviceAsync(
        SaltMonitorDbContext context,
        TimeProvider timeProvider)
    {
        var service = new EfFactoryDeviceRegistrationService(context, timeProvider);
        var result = await service.RegisterAsync(
            CreateFactoryRequest(SHA256.HashData(Encoding.UTF8.GetBytes("factory-secret"))),
            FactoryWorker);
        Assert.True(result.IsSuccess);
    }

    private static RegisterFactoryDeviceRequest CreateFactoryRequest(byte[] secretHash) =>
        new(
            "factory-test-job-0001",
            "Arduino Nano ESP32",
            "wf_boot_test_0001",
            Convert.ToBase64String(secretHash),
            "1.0.0",
            "pilot-v1");

    private static CreateCommissioningSessionRequest CreateSessionRequest() =>
        new(
            "WF-C-10482",
            "WF-L-10482-01",
            "WF-A-10482-S1",
            "WF-NANO-0001",
            "WO-BOOT-1001",
            150m);

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan duration) => now = now.Add(duration);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private TestDatabase(SaltMonitorDbContext context) => Context = context;

        public SaltMonitorDbContext Context { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var databaseName = $"WaterFlexBootstrapServiceTests_{Guid.NewGuid():N}";
            var connectionString = await TestPostgres.GetConnectionStringAsync(databaseName);
            var options = new DbContextOptionsBuilder<SaltMonitorDbContext>()
                .UseNpgsql(connectionString)
                .Options;
            var context = new SaltMonitorDbContext(options);
            await context.Database.MigrateAsync();
            return new(context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.Database.EnsureDeletedAsync();
            await Context.DisposeAsync();
        }
    }
}
