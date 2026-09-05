using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFactoryStationAndVerificationHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RedeemedByFactoryStationId",
                table: "FactoryFlashAuthorizations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FactoryStationEnrollmentGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SecretHash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PublicKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Thumbprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IssuedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FactoryStationEnrollmentGrants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FactoryStations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PublicKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Thumbprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    KeyProviderType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    HelperVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProtocolVersion = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EnrolledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FactoryStations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FactoryVerificationAuthorizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FactoryProvisioningJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    FactoryStationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CredentialId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SecretHash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    FirmwareVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConfigurationVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BundleSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IssuedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FactoryVerificationAuthorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FactoryVerificationAuthorizations_FactoryProvisioningJobs_F~",
                        column: x => x.FactoryProvisioningJobId,
                        principalTable: "FactoryProvisioningJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FactoryStationReplayNonces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FactoryStationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nonce = table.Column<string>(type: "character varying(22)", maxLength: 22, nullable: false),
                    UsedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FactoryStationReplayNonces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FactoryStationReplayNonces_FactoryStations_FactoryStationId",
                        column: x => x.FactoryStationId,
                        principalTable: "FactoryStations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FactoryProvisioningJobs_CreatedBy",
                table: "FactoryProvisioningJobs",
                column: "CreatedBy",
                unique: true,
                filter: "\"Status\" IN ('Registered', 'Quarantined')");

            migrationBuilder.CreateIndex(
                name: "IX_FactoryStationEnrollmentGrants_Thumbprint",
                table: "FactoryStationEnrollmentGrants",
                column: "Thumbprint");

            migrationBuilder.CreateIndex(
                name: "IX_FactoryStationReplayNonces_ExpiresAtUtc",
                table: "FactoryStationReplayNonces",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FactoryStationReplayNonces_FactoryStationId_Nonce",
                table: "FactoryStationReplayNonces",
                columns: new[] { "FactoryStationId", "Nonce" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FactoryStations_Thumbprint",
                table: "FactoryStations",
                column: "Thumbprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FactoryVerificationAuthorizations_CredentialId",
                table: "FactoryVerificationAuthorizations",
                column: "CredentialId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FactoryVerificationAuthorizations_FactoryProvisioningJobId",
                table: "FactoryVerificationAuthorizations",
                column: "FactoryProvisioningJobId");

            migrationBuilder.CreateIndex(
                name: "IX_FactoryVerificationAuthorizations_FactoryStationId",
                table: "FactoryVerificationAuthorizations",
                column: "FactoryStationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FactoryStationEnrollmentGrants");

            migrationBuilder.DropTable(
                name: "FactoryStationReplayNonces");

            migrationBuilder.DropTable(
                name: "FactoryVerificationAuthorizations");

            migrationBuilder.DropTable(
                name: "FactoryStations");

            migrationBuilder.DropIndex(
                name: "IX_FactoryProvisioningJobs_CreatedBy",
                table: "FactoryProvisioningJobs");

            migrationBuilder.DropColumn(
                name: "RedeemedByFactoryStationId",
                table: "FactoryFlashAuthorizations");
        }
    }
}
