using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPersistentInstallationWorkOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "InstallationWorkOrderNumberSequence");

            migrationBuilder.AddColumn<Guid>(
                name: "InstallationWorkOrderId",
                table: "CommissioningSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InstallationWorkOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NumberSequence = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('\"InstallationWorkOrderNumberSequence\"')"),
                    WorkOrderNumber = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    DealerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TankId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedByActorId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledByActorId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CancelledByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstallationWorkOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstallationWorkOrders_CustomerAccounts_CustomerAccountId",
                        column: x => x.CustomerAccountId,
                        principalTable: "CustomerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InstallationWorkOrders_Dealers_DealerId",
                        column: x => x.DealerId,
                        principalTable: "Dealers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InstallationWorkOrders_ServiceLocations_ServiceLocationId",
                        column: x => x.ServiceLocationId,
                        principalTable: "ServiceLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InstallationWorkOrders_Tanks_TankId",
                        column: x => x.TankId,
                        principalTable: "Tanks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommissioningSessions_InstallationWorkOrderId",
                table: "CommissioningSessions",
                column: "InstallationWorkOrderId",
                unique: true,
                filter: "\"InstallationWorkOrderId\" IS NOT NULL AND \"Status\" IN ('PendingSensor', 'ActivatedAwaitingHealth', 'AwaitingFirstTelemetry')");

            migrationBuilder.CreateIndex(
                name: "IX_InstallationWorkOrders_CustomerAccountId",
                table: "InstallationWorkOrders",
                column: "CustomerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_InstallationWorkOrders_DealerId_CreatedAtUtc",
                table: "InstallationWorkOrders",
                columns: new[] { "DealerId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InstallationWorkOrders_ServiceLocationId",
                table: "InstallationWorkOrders",
                column: "ServiceLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InstallationWorkOrders_TankId",
                table: "InstallationWorkOrders",
                column: "TankId");

            migrationBuilder.CreateIndex(
                name: "IX_InstallationWorkOrders_WorkOrderNumber",
                table: "InstallationWorkOrders",
                column: "WorkOrderNumber",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CommissioningSessions_InstallationWorkOrders_InstallationWo~",
                table: "CommissioningSessions",
                column: "InstallationWorkOrderId",
                principalTable: "InstallationWorkOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CommissioningSessions_InstallationWorkOrders_InstallationWo~",
                table: "CommissioningSessions");

            migrationBuilder.DropTable(
                name: "InstallationWorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_CommissioningSessions_InstallationWorkOrderId",
                table: "CommissioningSessions");

            migrationBuilder.DropColumn(
                name: "InstallationWorkOrderId",
                table: "CommissioningSessions");

            migrationBuilder.DropSequence(
                name: "InstallationWorkOrderNumberSequence");
        }
    }
}
