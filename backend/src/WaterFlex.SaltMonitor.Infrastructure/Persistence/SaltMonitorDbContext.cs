using Microsoft.EntityFrameworkCore;

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context for the salt monitor backend, covering provisioning, telemetry,
/// alerting, and staff access. Table mappings, string-backed enum conversions, unique/partial
/// indexes, and optimistic-concurrency row versions for every entity are all defined here in
/// <see cref="OnModelCreating"/> rather than via per-entity configuration classes.
/// </summary>
public sealed class SaltMonitorDbContext(DbContextOptions<SaltMonitorDbContext> options)
    : DbContext(options)
{
    public DbSet<Dealer> Dealers => Set<Dealer>();
    public DbSet<StaffIdentityRecord> StaffIdentities => Set<StaffIdentityRecord>();
    public DbSet<StaffInvitation> StaffInvitations => Set<StaffInvitation>();
    public DbSet<StaffAccessAuditEvent> StaffAccessAuditEvents => Set<StaffAccessAuditEvent>();
    public DbSet<StaffProvisioningWorkItem> StaffProvisioningWorkItems => Set<StaffProvisioningWorkItem>();
    public DbSet<CustomerAccount> CustomerAccounts => Set<CustomerAccount>();
    public DbSet<ServiceLocation> ServiceLocations => Set<ServiceLocation>();
    public DbSet<Tank> Tanks => Set<Tank>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<FactoryProvisioningJob> FactoryProvisioningJobs => Set<FactoryProvisioningJob>();
    public DbSet<DeviceBootstrapCredential> DeviceBootstrapCredentials => Set<DeviceBootstrapCredential>();
    public DbSet<DeviceCredential> DeviceCredentials => Set<DeviceCredential>();
    public DbSet<DeviceInstallation> DeviceInstallations => Set<DeviceInstallation>();
    public DbSet<CommissioningSession> CommissioningSessions => Set<CommissioningSession>();
    public DbSet<ProvisioningAuditEvent> ProvisioningAuditEvents => Set<ProvisioningAuditEvent>();
    public DbSet<TankCalibrationRecord> TankCalibrations => Set<TankCalibrationRecord>();
    public DbSet<TelemetryReadingRecord> TelemetryReadings => Set<TelemetryReadingRecord>();
    public DbSet<TelemetryHourlySummary> TelemetryHourlySummaries => Set<TelemetryHourlySummary>();
    public DbSet<TelemetryDailySummary> TelemetryDailySummaries => Set<TelemetryDailySummary>();
    public DbSet<TelemetryMaintenanceState> TelemetryMaintenanceStates => Set<TelemetryMaintenanceState>();
    public DbSet<LowSaltAlert> LowSaltAlerts => Set<LowSaltAlert>();
    public DbSet<LowSaltAlertAuditEvent> LowSaltAlertAuditEvents => Set<LowSaltAlertAuditEvent>();
    public DbSet<LowSaltAlertEvaluationState> LowSaltAlertEvaluationStates => Set<LowSaltAlertEvaluationState>();
    public DbSet<AlertEvaluationWorkItem> AlertEvaluationWorkItems => Set<AlertEvaluationWorkItem>();
    public DbSet<DeliveryTicket> DeliveryTickets => Set<DeliveryTicket>();
    public DbSet<DeliveryTicketWorkItem> DeliveryTicketWorkItems => Set<DeliveryTicketWorkItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StaffIdentityRecord>(entity =>
        {
            entity.ToTable("StaffIdentities");
            entity.HasKey(identity => identity.Id);
            entity.Property(identity => identity.Issuer).HasMaxLength(500);
            entity.Property(identity => identity.Subject).HasMaxLength(200);
            entity.Property(identity => identity.Email).HasMaxLength(320);
            entity.Property(identity => identity.NormalizedEmail).HasMaxLength(320);
            entity.Property(identity => identity.DisplayName).HasMaxLength(200);
            entity.Property(identity => identity.Role).HasConversion<string>().HasMaxLength(32);
            entity.Property(identity => identity.State).HasConversion<string>().HasMaxLength(32);
            entity.Property(identity => identity.CognitoUsername).HasMaxLength(200);
            entity.Property(identity => identity.RowVersion).IsRowVersion();
            entity.HasIndex(identity => new { identity.Issuer, identity.Subject }).IsUnique();
            entity.HasIndex(identity => identity.NormalizedEmail).IsUnique();
            entity.HasIndex(identity => new { identity.IsActive, identity.Role });
            entity.HasOne(identity => identity.Dealer)
                .WithMany()
                .HasForeignKey(identity => identity.DealerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StaffInvitation>(entity =>
        {
            entity.ToTable("StaffInvitations");
            entity.HasKey(invitation => invitation.Id);
            entity.Property(invitation => invitation.Email).HasMaxLength(320);
            entity.Property(invitation => invitation.NormalizedEmail).HasMaxLength(320);
            entity.Property(invitation => invitation.DisplayName).HasMaxLength(200);
            entity.Property(invitation => invitation.Role).HasConversion<string>().HasMaxLength(32);
            entity.Property(invitation => invitation.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(invitation => invitation.CreatedByStaffId).HasMaxLength(100);
            entity.Property(invitation => invitation.FailureReason).HasMaxLength(1000);
            entity.Property(invitation => invitation.RowVersion).IsRowVersion();
            entity.HasIndex(invitation => invitation.NormalizedEmail)
                .HasFilter("\"Status\" IN ('PendingProvisioning', 'Ready')")
                .IsUnique();
            entity.HasOne(invitation => invitation.Dealer).WithMany().HasForeignKey(invitation => invitation.DealerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(invitation => invitation.AcceptedStaffIdentity).WithMany().HasForeignKey(invitation => invitation.AcceptedStaffIdentityId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StaffAccessAuditEvent>(entity =>
        {
            entity.ToTable("StaffAccessAuditEvents");
            entity.HasKey(audit => audit.Id);
            entity.Property(audit => audit.EventType).HasMaxLength(100);
            entity.Property(audit => audit.ActorStaffId).HasMaxLength(100);
            entity.Property(audit => audit.Reason).HasMaxLength(500);
            entity.Property(audit => audit.DetailsJson).HasColumnType("jsonb");
            entity.HasIndex(audit => audit.OccurredAtUtc);
        });

        modelBuilder.Entity<StaffProvisioningWorkItem>(entity =>
        {
            entity.ToTable("StaffProvisioningWorkItems");
            entity.HasKey(work => work.Id);
            entity.Property(work => work.WorkType).HasMaxLength(100);
            entity.Property(work => work.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(work => work.IdempotencyKey).HasMaxLength(200);
            entity.Property(work => work.PayloadJson).HasColumnType("jsonb");
            entity.Property(work => work.LastError).HasMaxLength(2000);
            entity.HasIndex(work => work.IdempotencyKey).IsUnique();
            entity.HasIndex(work => new { work.Status, work.AvailableAtUtc });
        });

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
            entity.Property(device => device.Model).HasMaxLength(100);
            entity.Property(device => device.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(device => device.FactoryFirmwareVersion).HasMaxLength(64);
            entity.Property(device => device.FactoryConfigurationVersion).HasMaxLength(64);
            entity.Property(device => device.FactoryProvisionedBy).HasMaxLength(200);
            entity.Property(device => device.LastSensorStatus).HasConversion<string>().HasMaxLength(32);
            entity.Property(device => device.LastSensorFault).HasConversion<string>().HasMaxLength(32);
            entity.Property(device => device.LastHealthFirmwareVersion).HasMaxLength(64);
            entity.HasIndex(device => device.SerialNumber).IsUnique();
        });

        modelBuilder.Entity<FactoryProvisioningJob>(entity =>
        {
            entity.ToTable("FactoryProvisioningJobs");
            entity.HasKey(job => job.Id);
            entity.Property(job => job.IdempotencyKey).HasMaxLength(100);
            entity.Property(job => job.SerialNumber).HasMaxLength(64);
            entity.Property(job => job.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(job => job.CreatedBy).HasMaxLength(200);
            entity.Property(job => job.FailureCode).HasMaxLength(100);
            entity.Property(job => job.RowVersion).IsRowVersion();
            entity.HasIndex(job => job.IdempotencyKey).IsUnique();
            entity.HasIndex(job => job.SerialSequence).IsUnique();
            entity.HasIndex(job => job.SerialNumber).IsUnique();
            entity.HasOne(job => job.Device)
                .WithOne(device => device.FactoryProvisioningJob)
                .HasForeignKey<FactoryProvisioningJob>(job => job.DeviceId)
                .OnDelete(DeleteBehavior.Restrict);
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
                .HasFilter("\"RevokedAtUtc\" IS NULL AND \"ConsumedAtUtc\" IS NULL");
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
                .HasFilter("\"RemovedAtUtc\" IS NULL");
            entity.HasIndex(installation => installation.TankId)
                .IsUnique()
                .HasFilter("\"RemovedAtUtc\" IS NULL");
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
                .HasFilter("\"Status\" IN ('PendingSensor', 'ActivatedAwaitingHealth', 'AwaitingFirstTelemetry')");
            entity.HasIndex(session => session.TankId)
                .IsUnique()
                .HasFilter("\"Status\" IN ('PendingSensor', 'ActivatedAwaitingHealth', 'AwaitingFirstTelemetry')");
            entity.HasIndex(session => new { session.Status, session.ExpiresAtUtc });
            entity.HasIndex(session => session.ActivationAttemptId)
                .IsUnique()
                .HasFilter("\"ActivationAttemptId\" IS NOT NULL");
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
                .HasFilter("\"EffectiveToUtc\" IS NULL");
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
            entity.HasIndex(reading => new { reading.DeviceId, reading.ReceivedAtUtc, reading.Id })
                .IsDescending(false, true, true);
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

        ConfigureTelemetrySummary(modelBuilder.Entity<TelemetryHourlySummary>(), "TelemetryHourlySummaries");
        ConfigureTelemetrySummary(modelBuilder.Entity<TelemetryDailySummary>(), "TelemetryDailySummaries");

        modelBuilder.Entity<TelemetryMaintenanceState>(entity =>
        {
            entity.ToTable("TelemetryMaintenanceStates");
            entity.HasKey(state => state.Name);
            entity.Property(state => state.Name).HasMaxLength(100);
        });

        modelBuilder.Entity<LowSaltAlert>(entity =>
        {
            entity.ToTable("LowSaltAlerts");
            entity.HasKey(alert => alert.Id);
            entity.Property(alert => alert.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(alert => alert.AcknowledgedBy).HasMaxLength(200);
            entity.Property(alert => alert.ApprovedBy).HasMaxLength(200);
            entity.Property(alert => alert.DismissedBy).HasMaxLength(200);
            entity.Property(alert => alert.DismissalReason).HasMaxLength(500);
            entity.Property(alert => alert.RowVersion).IsRowVersion();
            entity.HasIndex(alert => new { alert.Status, alert.OpenedAtUtc });
            entity.HasIndex(alert => alert.DeviceInstallationId)
                .IsUnique()
                .HasFilter("\"Status\" IN ('Open', 'Acknowledged', 'Approved')");
            entity.HasOne(alert => alert.DeviceInstallation)
                .WithMany()
                .HasForeignKey(alert => alert.DeviceInstallationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LowSaltAlertAuditEvent>(entity =>
        {
            entity.ToTable("LowSaltAlertAuditEvents");
            entity.HasKey(auditEvent => auditEvent.Id);
            entity.Property(auditEvent => auditEvent.EventType).HasMaxLength(64);
            entity.Property(auditEvent => auditEvent.ActorType).HasMaxLength(32);
            entity.Property(auditEvent => auditEvent.ActorId).HasMaxLength(200);
            entity.Property(auditEvent => auditEvent.Reason).HasMaxLength(500);
            entity.HasIndex(auditEvent => new { auditEvent.LowSaltAlertId, auditEvent.OccurredAtUtc });
            entity.HasOne(auditEvent => auditEvent.LowSaltAlert)
                .WithMany(alert => alert.AuditEvents)
                .HasForeignKey(auditEvent => auditEvent.LowSaltAlertId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LowSaltAlertEvaluationState>(entity =>
        {
            entity.ToTable("LowSaltAlertEvaluationStates");
            entity.HasKey(state => state.DeviceInstallationId);
            entity.HasOne(state => state.DeviceInstallation)
                .WithMany()
                .HasForeignKey(state => state.DeviceInstallationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AlertEvaluationWorkItem>(entity =>
        {
            entity.ToTable("AlertEvaluationWorkItems");
            entity.HasKey(workItem => workItem.Id);
            entity.Property(workItem => workItem.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(workItem => workItem.LastError).HasMaxLength(1000);
            entity.HasIndex(workItem => workItem.TelemetryReadingId).IsUnique();
            entity.HasIndex(workItem => new { workItem.Status, workItem.AvailableAtUtc, workItem.Id });
            entity.HasOne(workItem => workItem.TelemetryReading)
                .WithMany()
                .HasForeignKey(workItem => workItem.TelemetryReadingId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DeliveryTicket>(entity =>
        {
            entity.ToTable("DeliveryTickets");
            entity.HasKey(ticket => ticket.Id);
            entity.Property(ticket => ticket.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(ticket => ticket.ExternalTicketId).HasMaxLength(200);
            entity.Property(ticket => ticket.IdempotencyKey).HasMaxLength(200);
            entity.Property(ticket => ticket.LastError).HasMaxLength(1000);
            entity.HasIndex(ticket => ticket.LowSaltAlertId).IsUnique();
            entity.HasIndex(ticket => ticket.IdempotencyKey).IsUnique();
            entity.HasOne(ticket => ticket.LowSaltAlert)
                .WithOne()
                .HasForeignKey<DeliveryTicket>(ticket => ticket.LowSaltAlertId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DeliveryTicketWorkItem>(entity =>
        {
            entity.ToTable("DeliveryTicketWorkItems");
            entity.HasKey(workItem => workItem.Id);
            entity.Property(workItem => workItem.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(workItem => workItem.LastError).HasMaxLength(1000);
            entity.HasIndex(workItem => workItem.DeliveryTicketId).IsUnique();
            entity.HasIndex(workItem => new { workItem.Status, workItem.AvailableAtUtc, workItem.Id });
            entity.HasOne(workItem => workItem.DeliveryTicket)
                .WithMany()
                .HasForeignKey(workItem => workItem.DeliveryTicketId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureTelemetrySummary<TSummary>(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TSummary> entity,
        string tableName)
        where TSummary : class
    {
        entity.ToTable(tableName);
        entity.HasKey("DeviceId", "BucketStartUtc");
        entity.Property("LatestFirmwareVersion").HasMaxLength(64);
        entity.HasIndex("BucketStartUtc");
        entity.HasOne(typeof(Device), "Device")
            .WithMany()
            .HasForeignKey("DeviceId")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
