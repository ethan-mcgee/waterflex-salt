using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPilotAlertWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AlertEvaluationWorkItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TelemetryReadingId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AvailableAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeasedUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertEvaluationWorkItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AlertEvaluationWorkItems_TelemetryReadings_TelemetryReading~",
                        column: x => x.TelemetryReadingId,
                        principalTable: "TelemetryReadings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LowSaltAlertEvaluationStates",
                columns: table => new
                {
                    DeviceInstallationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BelowEvidenceCount = table.Column<int>(type: "integer", nullable: false),
                    FirstBelowEvidenceAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RecoveryEvidenceCount = table.Column<int>(type: "integer", nullable: false),
                    FirstRecoveryEvidenceAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SuppressedUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastProcessedTelemetryReadingId = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LowSaltAlertEvaluationStates", x => x.DeviceInstallationId);
                    table.ForeignKey(
                        name: "FK_LowSaltAlertEvaluationStates_DeviceInstallations_DeviceInst~",
                        column: x => x.DeviceInstallationId,
                        principalTable: "DeviceInstallations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LowSaltAlerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceInstallationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OpenedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastEvidenceAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastEvidenceFillPercent = table.Column<double>(type: "double precision", nullable: false),
                    AcknowledgedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcknowledgedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DismissedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DismissedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DismissalReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LowSaltAlerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LowSaltAlerts_DeviceInstallations_DeviceInstallationId",
                        column: x => x.DeviceInstallationId,
                        principalTable: "DeviceInstallations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LowSaltAlertAuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LowSaltAlertId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActorId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TelemetryReadingId = table.Column<long>(type: "bigint", nullable: true),
                    FillPercent = table.Column<double>(type: "double precision", nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LowSaltAlertAuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LowSaltAlertAuditEvents_LowSaltAlerts_LowSaltAlertId",
                        column: x => x.LowSaltAlertId,
                        principalTable: "LowSaltAlerts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AlertEvaluationWorkItems_Status_AvailableAtUtc_Id",
                table: "AlertEvaluationWorkItems",
                columns: new[] { "Status", "AvailableAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AlertEvaluationWorkItems_TelemetryReadingId",
                table: "AlertEvaluationWorkItems",
                column: "TelemetryReadingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LowSaltAlertAuditEvents_LowSaltAlertId_OccurredAtUtc",
                table: "LowSaltAlertAuditEvents",
                columns: new[] { "LowSaltAlertId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LowSaltAlerts_DeviceInstallationId",
                table: "LowSaltAlerts",
                column: "DeviceInstallationId",
                unique: true,
                filter: "\"Status\" IN ('Open', 'Acknowledged', 'Approved')");

            migrationBuilder.CreateIndex(
                name: "IX_LowSaltAlerts_Status_OpenedAtUtc",
                table: "LowSaltAlerts",
                columns: new[] { "Status", "OpenedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlertEvaluationWorkItems");

            migrationBuilder.DropTable(
                name: "LowSaltAlertAuditEvents");

            migrationBuilder.DropTable(
                name: "LowSaltAlertEvaluationStates");

            migrationBuilder.DropTable(
                name: "LowSaltAlerts");
        }
    }
}
