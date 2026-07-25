using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBootstrapProvisioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FactoryConfigurationVersion",
                table: "Devices",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FactoryFirmwareVersion",
                table: "Devices",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FactoryProvisionedBy",
                table: "Devices",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CommissioningSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DealerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TankId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProvisionalCredentialId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TankDepthMm = table.Column<int>(type: "int", nullable: false),
                    WaterFlexWorkOrderId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedByActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedByDisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActivatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ActivationAttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
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
                        name: "FK_CommissioningSessions_DeviceCredentials_ProvisionalCredentialId",
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
                name: "DeviceBootstrapCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CredentialId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SecretHash = table.Column<byte[]>(type: "varbinary(32)", maxLength: 32, nullable: false),
                    ValidFromUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastUsedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FailedAttemptCount = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
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
                name: "ProvisioningAuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CommissioningSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActorType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DetailsJson = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProvisioningAuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProvisioningAuditEvents_CommissioningSessions_CommissioningSessionId",
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

            migrationBuilder.CreateIndex(
                name: "IX_CommissioningSessions_ActivationAttemptId",
                table: "CommissioningSessions",
                column: "ActivationAttemptId",
                unique: true,
                filter: "[ActivationAttemptId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CommissioningSessions_DealerId",
                table: "CommissioningSessions",
                column: "DealerId");

            migrationBuilder.CreateIndex(
                name: "IX_CommissioningSessions_DeviceId",
                table: "CommissioningSessions",
                column: "DeviceId",
                unique: true,
                filter: "[Status] IN ('PendingSensor', 'AwaitingFirstTelemetry')");

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
                filter: "[Status] IN ('PendingSensor', 'AwaitingFirstTelemetry')");

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
                filter: "[RevokedAtUtc] IS NULL AND [ConsumedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningAuditEvents_CommissioningSessionId_OccurredAtUtc",
                table: "ProvisioningAuditEvents",
                columns: new[] { "CommissioningSessionId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningAuditEvents_DeviceId_OccurredAtUtc",
                table: "ProvisioningAuditEvents",
                columns: new[] { "DeviceId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceBootstrapCredentials");

            migrationBuilder.DropTable(
                name: "ProvisioningAuditEvents");

            migrationBuilder.DropTable(
                name: "CommissioningSessions");

            migrationBuilder.DropColumn(
                name: "FactoryConfigurationVersion",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "FactoryFirmwareVersion",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "FactoryProvisionedBy",
                table: "Devices");
        }
    }
}
