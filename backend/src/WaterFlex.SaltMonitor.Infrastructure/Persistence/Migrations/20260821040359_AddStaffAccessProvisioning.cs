using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffAccessProvisioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ActivatedAtUtc",
                table: "StaffIdentities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CognitoUsername",
                table: "StaffIdentities",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedEmail",
                table: "StaffIdentities",
                type: "character varying(320)",
                maxLength: 320,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "StaffIdentities",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SuspendedAtUtc",
                table: "StaffIdentities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "StaffIdentities"
                SET "NormalizedEmail" = UPPER(TRIM("Email")),
                    "State" = CASE WHEN "IsActive" THEN 'Active' ELSE 'Suspended' END,
                    "ActivatedAtUtc" = CASE WHEN "IsActive" THEN "CreatedAtUtc" ELSE NULL END,
                    "SuspendedAtUtc" = CASE WHEN "IsActive" THEN NULL ELSE "UpdatedAtUtc" END;
                """);

            migrationBuilder.CreateTable(
                name: "StaffAccessAuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ActorStaffId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TargetStaffIdentityId = table.Column<Guid>(type: "uuid", nullable: true),
                    InvitationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DetailsJson = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffAccessAuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StaffInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DealerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedByStaffId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcceptedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcceptedStaffIdentityId = table.Column<Guid>(type: "uuid", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffInvitations_Dealers_DealerId",
                        column: x => x.DealerId,
                        principalTable: "Dealers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffInvitations_StaffIdentities_AcceptedStaffIdentityId",
                        column: x => x.AcceptedStaffIdentityId,
                        principalTable: "StaffIdentities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffProvisioningWorkItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WorkType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StaffIdentityId = table.Column<Guid>(type: "uuid", nullable: true),
                    InvitationId = table.Column<Guid>(type: "uuid", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    AvailableAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffProvisioningWorkItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StaffIdentities_NormalizedEmail",
                table: "StaffIdentities",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffAccessAuditEvents_OccurredAtUtc",
                table: "StaffAccessAuditEvents",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StaffInvitations_AcceptedStaffIdentityId",
                table: "StaffInvitations",
                column: "AcceptedStaffIdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffInvitations_DealerId",
                table: "StaffInvitations",
                column: "DealerId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffInvitations_NormalizedEmail",
                table: "StaffInvitations",
                column: "NormalizedEmail",
                unique: true,
                filter: "\"Status\" IN ('PendingProvisioning', 'Ready')");

            migrationBuilder.CreateIndex(
                name: "IX_StaffProvisioningWorkItems_IdempotencyKey",
                table: "StaffProvisioningWorkItems",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffProvisioningWorkItems_Status_AvailableAtUtc",
                table: "StaffProvisioningWorkItems",
                columns: new[] { "Status", "AvailableAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StaffAccessAuditEvents");

            migrationBuilder.DropTable(
                name: "StaffInvitations");

            migrationBuilder.DropTable(
                name: "StaffProvisioningWorkItems");

            migrationBuilder.DropIndex(
                name: "IX_StaffIdentities_NormalizedEmail",
                table: "StaffIdentities");

            migrationBuilder.DropColumn(
                name: "ActivatedAtUtc",
                table: "StaffIdentities");

            migrationBuilder.DropColumn(
                name: "CognitoUsername",
                table: "StaffIdentities");

            migrationBuilder.DropColumn(
                name: "NormalizedEmail",
                table: "StaffIdentities");

            migrationBuilder.DropColumn(
                name: "State",
                table: "StaffIdentities");

            migrationBuilder.DropColumn(
                name: "SuspendedAtUtc",
                table: "StaffIdentities");
        }
    }
}
