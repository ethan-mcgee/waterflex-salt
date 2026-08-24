using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WaterFlexCustomerId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AccountNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastSyncedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Dealers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dealers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SerialNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    HardwareId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RegisteredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CommissionedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RetiredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FactoryFirmwareVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FactoryConfigurationVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FactoryProvisionedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServiceLocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    WaterFlexLocationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AddressSummary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastSyncedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceLocations_CustomerAccounts_CustomerAccountId",
                        column: x => x.CustomerAccountId,
                        principalTable: "CustomerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeviceBootstrapCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CredentialId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SecretHash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    ValidFromUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastUsedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailedAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceBootstrapCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceBootstrapCredentials_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeviceCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CredentialId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SecretHash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    ValidFromUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastUsedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceCredentials_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Tanks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    WaterFlexAssetId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CapacityPounds = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tanks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tanks_ServiceLocations_ServiceLocationId",
                        column: x => x.ServiceLocationId,
                        principalTable: "ServiceLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommissioningSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DealerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TankId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProvisionalCredentialId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TankDepthMm = table.Column<int>(type: "integer", nullable: false),
                    WaterFlexWorkOrderId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedByActorId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActivatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActivationAttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommissioningSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommissioningSessions_Dealers_DealerId",
                        column: x => x.DealerId,
                        principalTable: "Dealers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommissioningSessions_DeviceCredentials_ProvisionalCredenti~",
                        column: x => x.ProvisionalCredentialId,
                        principalTable: "DeviceCredentials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommissioningSessions_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommissioningSessions_Tanks_TankId",
                        column: x => x.TankId,
                        principalTable: "Tanks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeviceInstallations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    TankId = table.Column<Guid>(type: "uuid", nullable: false),
                    DealerId = table.Column<Guid>(type: "uuid", nullable: true),
                    InstalledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RemovedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    InstalledBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    WaterFlexWorkOrderId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceInstallations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceInstallations_Dealers_DealerId",
                        column: x => x.DealerId,
                        principalTable: "Dealers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeviceInstallations_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeviceInstallations_Tanks_TankId",
                        column: x => x.TankId,
                        principalTable: "Tanks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProvisioningAuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CommissioningSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActorId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DetailsJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProvisioningAuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProvisioningAuditEvents_CommissioningSessions_Commissioning~",
                        column: x => x.CommissioningSessionId,
                        principalTable: "CommissioningSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProvisioningAuditEvents_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TankCalibrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceInstallationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    TankDepthMm = table.Column<int>(type: "integer", nullable: false),
                    CommissioningDistanceMm = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFromUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TankCalibrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TankCalibrations_DeviceInstallations_DeviceInstallationId",
                        column: x => x.DeviceInstallationId,
                        principalTable: "DeviceInstallations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TelemetryReadings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceInstallationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TankCalibrationRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    BootId = table.Column<Guid>(type: "uuid", nullable: false),
                    SequenceNumber = table.Column<long>(type: "bigint", nullable: false),
                    ObservedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UptimeMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    RawDistanceMm = table.Column<int>(type: "integer", nullable: false),
                    FillPercent = table.Column<double>(type: "double precision", nullable: false),
                    Quality = table.Column<int>(type: "integer", nullable: false),
                    SampleCount = table.Column<int>(type: "integer", nullable: false),
                    WifiRssiDbm = table.Column<int>(type: "integer", nullable: false),
                    FirmwareVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ErrorFlagsJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryReadings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TelemetryReadings_DeviceInstallations_DeviceInstallationId",
                        column: x => x.DeviceInstallationId,
                        principalTable: "DeviceInstallations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TelemetryReadings_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TelemetryReadings_TankCalibrations_TankCalibrationRecordId",
                        column: x => x.TankCalibrationRecordId,
                        principalTable: "TankCalibrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommissioningSessions_ActivationAttemptId",
                table: "CommissioningSessions",
                column: "ActivationAttemptId",
                unique: true,
                filter: "\"ActivationAttemptId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CommissioningSessions_DealerId",
                table: "CommissioningSessions",
                column: "DealerId");

            migrationBuilder.CreateIndex(
                name: "IX_CommissioningSessions_DeviceId",
                table: "CommissioningSessions",
                column: "DeviceId",
                unique: true,
                filter: "\"Status\" IN ('PendingSensor', 'AwaitingFirstTelemetry')");

            migrationBuilder.CreateIndex(
                name: "IX_CommissioningSessions_ProvisionalCredentialId",
                table: "CommissioningSessions",
                column: "ProvisionalCredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_CommissioningSessions_Status_ExpiresAtUtc",
                table: "CommissioningSessions",
                columns: new[] { "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CommissioningSessions_TankId",
                table: "CommissioningSessions",
                column: "TankId",
                unique: true,
                filter: "\"Status\" IN ('PendingSensor', 'AwaitingFirstTelemetry')");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAccounts_WaterFlexCustomerId",
                table: "CustomerAccounts",
                column: "WaterFlexCustomerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Dealers_ExternalId",
                table: "Dealers",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceBootstrapCredentials_CredentialId",
                table: "DeviceBootstrapCredentials",
                column: "CredentialId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceBootstrapCredentials_DeviceId",
                table: "DeviceBootstrapCredentials",
                column: "DeviceId",
                unique: true,
                filter: "\"RevokedAtUtc\" IS NULL AND \"ConsumedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceCredentials_CredentialId",
                table: "DeviceCredentials",
                column: "CredentialId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceCredentials_DeviceId",
                table: "DeviceCredentials",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceInstallations_DealerId",
                table: "DeviceInstallations",
                column: "DealerId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceInstallations_DeviceId",
                table: "DeviceInstallations",
                column: "DeviceId",
                unique: true,
                filter: "\"RemovedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceInstallations_TankId",
                table: "DeviceInstallations",
                column: "TankId",
                unique: true,
                filter: "\"RemovedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_HardwareId",
                table: "Devices",
                column: "HardwareId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Devices_SerialNumber",
                table: "Devices",
                column: "SerialNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningAuditEvents_CommissioningSessionId_OccurredAtUtc",
                table: "ProvisioningAuditEvents",
                columns: new[] { "CommissioningSessionId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningAuditEvents_DeviceId_OccurredAtUtc",
                table: "ProvisioningAuditEvents",
                columns: new[] { "DeviceId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceLocations_CustomerAccountId_WaterFlexLocationId",
                table: "ServiceLocations",
                columns: new[] { "CustomerAccountId", "WaterFlexLocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TankCalibrations_DeviceInstallationId",
                table: "TankCalibrations",
                column: "DeviceInstallationId",
                unique: true,
                filter: "\"EffectiveToUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TankCalibrations_DeviceInstallationId_Version",
                table: "TankCalibrations",
                columns: new[] { "DeviceInstallationId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tanks_ServiceLocationId",
                table: "Tanks",
                column: "ServiceLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryReadings_DeviceId_BootId_SequenceNumber",
                table: "TelemetryReadings",
                columns: new[] { "DeviceId", "BootId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryReadings_DeviceInstallationId_ReceivedAtUtc",
                table: "TelemetryReadings",
                columns: new[] { "DeviceInstallationId", "ReceivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryReadings_TankCalibrationRecordId",
                table: "TelemetryReadings",
                column: "TankCalibrationRecordId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceBootstrapCredentials");

            migrationBuilder.DropTable(
                name: "ProvisioningAuditEvents");

            migrationBuilder.DropTable(
                name: "TelemetryReadings");

            migrationBuilder.DropTable(
                name: "CommissioningSessions");

            migrationBuilder.DropTable(
                name: "TankCalibrations");

            migrationBuilder.DropTable(
                name: "DeviceCredentials");

            migrationBuilder.DropTable(
                name: "DeviceInstallations");

            migrationBuilder.DropTable(
                name: "Dealers");

            migrationBuilder.DropTable(
                name: "Devices");

            migrationBuilder.DropTable(
                name: "Tanks");

            migrationBuilder.DropTable(
                name: "ServiceLocations");

            migrationBuilder.DropTable(
                name: "CustomerAccounts");
        }
    }
}
