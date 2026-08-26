using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryTicketOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeliveryTickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LowSaltAlertId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExternalTicketId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExternalCreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryTickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryTickets_LowSaltAlerts_LowSaltAlertId",
                        column: x => x.LowSaltAlertId,
                        principalTable: "LowSaltAlerts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryTicketWorkItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeliveryTicketId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_DeliveryTicketWorkItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryTicketWorkItems_DeliveryTickets_DeliveryTicketId",
                        column: x => x.DeliveryTicketId,
                        principalTable: "DeliveryTickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTickets_IdempotencyKey",
                table: "DeliveryTickets",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTickets_LowSaltAlertId",
                table: "DeliveryTickets",
                column: "LowSaltAlertId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTicketWorkItems_DeliveryTicketId",
                table: "DeliveryTicketWorkItems",
                column: "DeliveryTicketId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTicketWorkItems_Status_AvailableAtUtc_Id",
                table: "DeliveryTicketWorkItems",
                columns: new[] { "Status", "AvailableAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeliveryTicketWorkItems");

            migrationBuilder.DropTable(
                name: "DeliveryTickets");
        }
    }
}
