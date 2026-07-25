using Microsoft.EntityFrameworkCore;

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence;

public sealed class SaltMonitorDbContext(DbContextOptions<SaltMonitorDbContext> options)
    : DbContext(options)
{
    public DbSet<Dealer> Dealers => Set<Dealer>();
    public DbSet<CustomerAccount> CustomerAccounts => Set<CustomerAccount>();
    public DbSet<ServiceLocation> ServiceLocations => Set<ServiceLocation>();
    public DbSet<Tank> Tanks => Set<Tank>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DeviceBootstrapCredential> DeviceBootstrapCredentials => Set<DeviceBootstrapCredential>();
    public DbSet<DeviceCredential> DeviceCredentials => Set<DeviceCredential>();
    public DbSet<DeviceInstallation> DeviceInstallations => Set<DeviceInstallation>();
    public DbSet<CommissioningSession> CommissioningSessions => Set<CommissioningSession>();
    public DbSet<ProvisioningAuditEvent> ProvisioningAuditEvents => Set<ProvisioningAuditEvent>();
    public DbSet<TankCalibrationRecord> TankCalibrations => Set<TankCalibrationRecord>();
    public DbSet<TelemetryReadingRecord> TelemetryReadings => Set<TelemetryReadingRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Dealer>(entity =>
        {
            entity.ToTable("Dealers");
            entity.HasKey(dealer => dealer.Id);
            entity.Property(dealer => dealer.ExternalId).HasMaxLength(64);
            entity.Property(dealer => dealer.DisplayName).HasMaxLength(200);
            entity.HasIndex(dealer => dealer.ExternalId).IsUnique();
        });

        modelBuilder.Entity<CustomerAccount>(entity =>
        {
            entity.ToTable("CustomerAccounts");
            entity.HasKey(customer => customer.Id);
            entity.Property(customer => customer.WaterFlexCustomerId).HasMaxLength(128);
            entity.Property(customer => customer.AccountNumber).HasMaxLength(64);
            entity.Property(customer => customer.DisplayName).HasMaxLength(200);
            entity.HasIndex(customer => customer.WaterFlexCustomerId).IsUnique();
        });

        modelBuilder.Entity<ServiceLocation>(entity =>
        {
            entity.ToTable("ServiceLocations");
            entity.HasKey(location => location.Id);
            entity.Property(location => location.WaterFlexLocationId).HasMaxLength(128);
            entity.Property(location => location.DisplayName).HasMaxLength(200);
            entity.Property(location => location.AddressSummary).HasMaxLength(500);
            entity.HasIndex(location => new { location.CustomerAccountId, location.WaterFlexLocationId }).IsUnique();
            entity.HasOne(location => location.CustomerAccount)
                .WithMany(customer => customer.ServiceLocations)
                .HasForeignKey(location => location.CustomerAccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Tank>(entity =>
        {
            entity.ToTable("Tanks");
            entity.HasKey(tank => tank.Id);
            entity.Property(tank => tank.WaterFlexAssetId).HasMaxLength(128);
            entity.Property(tank => tank.Label).HasMaxLength(100);
            entity.HasOne(tank => tank.ServiceLocation)
                .WithMany(location => location.Tanks)
                .HasForeignKey(tank => tank.ServiceLocationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Device>(entity =>
        {
            entity.ToTable("Devices");
            entity.HasKey(device => device.Id);
            entity.Property(device => device.SerialNumber).HasMaxLength(64);
            entity.Property(device => device.HardwareId).HasMaxLength(32);
            entity.Property(device => device.Model).HasMaxLength(100);
            entity.Property(device => device.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(device => device.FactoryFirmwareVersion).HasMaxLength(64);
            entity.Property(device => device.FactoryConfigurationVersion).HasMaxLength(64);
            entity.Property(device => device.FactoryProvisionedBy).HasMaxLength(200);
            entity.HasIndex(device => device.SerialNumber).IsUnique();
            entity.HasIndex(device => device.HardwareId).IsUnique();
        });

        modelBuilder.Entity<DeviceBootstrapCredential>(entity =>
        {
            entity.ToTable("DeviceBootstrapCredentials");
            entity.HasKey(credential => credential.Id);
            entity.Property(credential => credential.CredentialId).HasMaxLength(64);
            entity.Property(credential => credential.SecretHash).HasMaxLength(32);
            entity.Property(credential => credential.RowVersion).IsRowVersion();
            entity.HasIndex(credential => credential.CredentialId).IsUnique();
            entity.HasIndex(credential => credential.DeviceId)
                .IsUnique()
                .HasFilter("[RevokedAtUtc] IS NULL AND [ConsumedAtUtc] IS NULL");
            entity.HasOne(credential => credential.Device)
                .WithMany(device => device.BootstrapCredentials)
                .HasForeignKey(credential => credential.DeviceId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DeviceCredential>(entity =>
        {
            entity.ToTable("DeviceCredentials");
            entity.HasKey(credential => credential.Id);
            entity.Property(credential => credential.CredentialId).HasMaxLength(64);
            entity.Property(credential => credential.SecretHash).HasMaxLength(32);
            entity.HasIndex(credential => credential.CredentialId).IsUnique();
            entity.HasOne(credential => credential.Device)
                .WithMany(device => device.Credentials)
                .HasForeignKey(credential => credential.DeviceId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DeviceInstallation>(entity =>
        {
            entity.ToTable("DeviceInstallations");
            entity.HasKey(installation => installation.Id);
            entity.Property(installation => installation.InstalledBy).HasMaxLength(200);
            entity.Property(installation => installation.WaterFlexWorkOrderId).HasMaxLength(128);
            entity.Property(installation => installation.RowVersion).IsRowVersion();
            entity.HasIndex(installation => installation.DeviceId)
                .IsUnique()
                .HasFilter("[RemovedAtUtc] IS NULL");
            entity.HasIndex(installation => installation.TankId)
                .IsUnique()
                .HasFilter("[RemovedAtUtc] IS NULL");
            entity.HasIndex(installation => installation.DealerId);
            entity.HasOne(installation => installation.Device)
                .WithMany(device => device.Installations)
                .HasForeignKey(installation => installation.DeviceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(installation => installation.Tank)
                .WithMany(tank => tank.Installations)
                .HasForeignKey(installation => installation.TankId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(installation => installation.Dealer)
                .WithMany(dealer => dealer.Installations)
                .HasForeignKey(installation => installation.DealerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CommissioningSession>(entity =>
        {
            entity.ToTable("CommissioningSessions");
            entity.HasKey(session => session.Id);
            entity.Property(session => session.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(session => session.WaterFlexWorkOrderId).HasMaxLength(128);
            entity.Property(session => session.CreatedByActorId).HasMaxLength(128);
            entity.Property(session => session.CreatedByDisplayName).HasMaxLength(200);
            entity.Property(session => session.FailureCode).HasMaxLength(64);
            entity.Property(session => session.RowVersion).IsRowVersion();
            entity.HasIndex(session => session.DeviceId)
                .IsUnique()
                .HasFilter("[Status] IN ('PendingSensor', 'AwaitingFirstTelemetry')");
            entity.HasIndex(session => session.TankId)
                .IsUnique()
                .HasFilter("[Status] IN ('PendingSensor', 'AwaitingFirstTelemetry')");
            entity.HasIndex(session => new { session.Status, session.ExpiresAtUtc });
            entity.HasIndex(session => session.ActivationAttemptId)
                .IsUnique()
                .HasFilter("[ActivationAttemptId] IS NOT NULL");
            entity.HasOne(session => session.Device)
                .WithMany(device => device.CommissioningSessions)
                .HasForeignKey(session => session.DeviceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(session => session.Dealer)
                .WithMany()
                .HasForeignKey(session => session.DealerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(session => session.Tank)
                .WithMany()
                .HasForeignKey(session => session.TankId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(session => session.ProvisionalCredential)
                .WithMany()
                .HasForeignKey(session => session.ProvisionalCredentialId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProvisioningAuditEvent>(entity =>
        {
            entity.ToTable("ProvisioningAuditEvents");
            entity.HasKey(auditEvent => auditEvent.Id);
            entity.Property(auditEvent => auditEvent.EventType).HasMaxLength(64);
            entity.Property(auditEvent => auditEvent.ActorType).HasMaxLength(32);
            entity.Property(auditEvent => auditEvent.ActorId).HasMaxLength(128);
            entity.Property(auditEvent => auditEvent.DetailsJson).HasMaxLength(2048);
            entity.HasIndex(auditEvent => new { auditEvent.DeviceId, auditEvent.OccurredAtUtc });
            entity.HasIndex(auditEvent => new { auditEvent.CommissioningSessionId, auditEvent.OccurredAtUtc });
            entity.HasOne(auditEvent => auditEvent.Device)
                .WithMany()
                .HasForeignKey(auditEvent => auditEvent.DeviceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(auditEvent => auditEvent.CommissioningSession)
                .WithMany(session => session.AuditEvents)
                .HasForeignKey(auditEvent => auditEvent.CommissioningSessionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TankCalibrationRecord>(entity =>
        {
            entity.ToTable("TankCalibrations");
            entity.HasKey(calibration => calibration.Id);
            entity.Property(calibration => calibration.CreatedBy).HasMaxLength(200);
            entity.HasIndex(calibration => new { calibration.DeviceInstallationId, calibration.Version }).IsUnique();
            entity.HasIndex(calibration => calibration.DeviceInstallationId)
                .IsUnique()
                .HasFilter("[EffectiveToUtc] IS NULL");
            entity.HasOne(calibration => calibration.DeviceInstallation)
                .WithMany(installation => installation.Calibrations)
                .HasForeignKey(calibration => calibration.DeviceInstallationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TelemetryReadingRecord>(entity =>
        {
            entity.ToTable("TelemetryReadings");
            entity.HasKey(reading => reading.Id);
            entity.Property(reading => reading.FirmwareVersion).HasMaxLength(64);
            entity.Property(reading => reading.ErrorFlagsJson).HasMaxLength(2048);
            entity.HasIndex(reading => new { reading.DeviceId, reading.BootId, reading.SequenceNumber }).IsUnique();
            entity.HasIndex(reading => new { reading.DeviceInstallationId, reading.ReceivedAtUtc });
            entity.HasOne(reading => reading.Device)
                .WithMany()
                .HasForeignKey(reading => reading.DeviceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(reading => reading.DeviceInstallation)
                .WithMany()
                .HasForeignKey(reading => reading.DeviceInstallationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(reading => reading.TankCalibrationRecord)
                .WithMany()
                .HasForeignKey(reading => reading.TankCalibrationRecordId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}