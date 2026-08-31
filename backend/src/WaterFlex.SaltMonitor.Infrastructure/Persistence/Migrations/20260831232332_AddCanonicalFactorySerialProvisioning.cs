using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCanonicalFactorySerialProvisioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Devices_HardwareId",
                table: "Devices");

            migrationBuilder.DropIndex(
                name: "IX_CommissioningSessions_DeviceId",
                table: "CommissioningSessions");

            migrationBuilder.DropIndex(
                name: "IX_CommissioningSessions_TankId",
                table: "CommissioningSessions");

            migrationBuilder.DropColumn(
                name: "HardwareId",
                table: "Devices");

            migrationBuilder.CreateTable(
                name: "FactoryProvisioningJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SerialSequence = table.Column<long>(type: "bigint", nullable: false),
                    SerialNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VerifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FactoryProvisioningJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FactoryProvisioningJobs_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO "FactoryProvisioningJobs"
                    ("Id", "IdempotencyKey", "SerialSequence", "SerialNumber", "Status", "DeviceId", "CreatedBy", "CreatedAtUtc")
                SELECT gen_random_uuid(),
                       'legacy-' || "Id"::text,
                       substring("SerialNumber" from 9)::bigint,
                       "SerialNumber",
                       CASE WHEN "Status" = 'Active' THEN 'Provisioned' ELSE 'Registered' END,
                       "Id",
                       COALESCE("FactoryProvisionedBy", 'legacy-migration'),
                       "RegisteredAtUtc"
                FROM "Devices"
                WHERE "SerialNumber" ~ '^WF-NANO-[0-9]+$';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_CommissioningSessions_DeviceId",
                table: "CommissioningSessions",
                column: "DeviceId",
                unique: true,
                filter: "\"Status\" IN ('PendingSensor', 'ActivatedAwaitingHealth', 'AwaitingFirstTelemetry')");

            migrationBuilder.CreateIndex(
                name: "IX_CommissioningSessions_TankId",
                table: "CommissioningSessions",
                column: "TankId",
                unique: true,
                filter: "\"Status\" IN ('PendingSensor', 'ActivatedAwaitingHealth', 'AwaitingFirstTelemetry')");

            migrationBuilder.CreateIndex(
                name: "IX_FactoryProvisioningJobs_DeviceId",
                table: "FactoryProvisioningJobs",
                column: "DeviceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FactoryProvisioningJobs_IdempotencyKey",
                table: "FactoryProvisioningJobs",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FactoryProvisioningJobs_SerialNumber",
                table: "FactoryProvisioningJobs",
                column: "SerialNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FactoryProvisioningJobs_SerialSequence",
                table: "FactoryProvisioningJobs",
                column: "SerialSequence",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FactoryProvisioningJobs");

            migrationBuilder.DropIndex(
                name: "IX_CommissioningSessions_DeviceId",
                table: "CommissioningSessions");

            migrationBuilder.DropIndex(
                name: "IX_CommissioningSessions_TankId",
                table: "CommissioningSessions");

            migrationBuilder.AddColumn<string>(
                name: "HardwareId",
                table: "Devices",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_HardwareId",
                table: "Devices",
                column: "HardwareId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommissioningSessions_DeviceId",
                table: "CommissioningSessions",
                column: "DeviceId",
                unique: true,
                filter: "\"Status\" IN ('PendingSensor', 'AwaitingFirstTelemetry')");

            migrationBuilder.CreateIndex(
                name: "IX_CommissioningSessions_TankId",
                table: "CommissioningSessions",
                column: "TankId",
                unique: true,
                filter: "\"Status\" IN ('PendingSensor', 'AwaitingFirstTelemetry')");
        }
    }
}
