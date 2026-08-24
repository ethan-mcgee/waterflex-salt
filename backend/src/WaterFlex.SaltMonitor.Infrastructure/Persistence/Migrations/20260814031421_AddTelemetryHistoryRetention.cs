using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTelemetryHistoryRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TelemetryDailySummaries",
                columns: table => new
                {
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    BucketStartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastReadingAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadingCount = table.Column<long>(type: "bigint", nullable: false),
                    FillPercentMin = table.Column<double>(type: "double precision", nullable: false),
                    FillPercentMax = table.Column<double>(type: "double precision", nullable: false),
                    FillPercentAverage = table.Column<double>(type: "double precision", nullable: false),
                    FillPercentLatest = table.Column<double>(type: "double precision", nullable: false),
                    RawDistanceMmMin = table.Column<int>(type: "integer", nullable: false),
                    RawDistanceMmMax = table.Column<int>(type: "integer", nullable: false),
                    RawDistanceMmAverage = table.Column<double>(type: "double precision", nullable: false),
                    WifiRssiDbmMin = table.Column<int>(type: "integer", nullable: false),
                    WifiRssiDbmMax = table.Column<int>(type: "integer", nullable: false),
                    WifiRssiDbmAverage = table.Column<double>(type: "double precision", nullable: false),
                    WorstQuality = table.Column<int>(type: "integer", nullable: false),
                    ErrorCount = table.Column<long>(type: "bigint", nullable: false),
                    LatestFirmwareVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryDailySummaries", x => new { x.DeviceId, x.BucketStartUtc });
                    table.ForeignKey(
                        name: "FK_TelemetryDailySummaries_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TelemetryHourlySummaries",
                columns: table => new
                {
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    BucketStartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastReadingAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadingCount = table.Column<long>(type: "bigint", nullable: false),
                    FillPercentMin = table.Column<double>(type: "double precision", nullable: false),
                    FillPercentMax = table.Column<double>(type: "double precision", nullable: false),
                    FillPercentAverage = table.Column<double>(type: "double precision", nullable: false),
                    FillPercentLatest = table.Column<double>(type: "double precision", nullable: false),
                    RawDistanceMmMin = table.Column<int>(type: "integer", nullable: false),
                    RawDistanceMmMax = table.Column<int>(type: "integer", nullable: false),
                    RawDistanceMmAverage = table.Column<double>(type: "double precision", nullable: false),
                    WifiRssiDbmMin = table.Column<int>(type: "integer", nullable: false),
                    WifiRssiDbmMax = table.Column<int>(type: "integer", nullable: false),
                    WifiRssiDbmAverage = table.Column<double>(type: "double precision", nullable: false),
                    WorstQuality = table.Column<int>(type: "integer", nullable: false),
                    ErrorCount = table.Column<long>(type: "bigint", nullable: false),
                    LatestFirmwareVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryHourlySummaries", x => new { x.DeviceId, x.BucketStartUtc });
                    table.ForeignKey(
                        name: "FK_TelemetryHourlySummaries_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TelemetryMaintenanceStates",
                columns: table => new
                {
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryMaintenanceStates", x => x.Name);
                });

            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY \"IX_TelemetryReadings_DeviceId_ReceivedAtUtc_Id\" " +
                "ON \"TelemetryReadings\" (\"DeviceId\", \"ReceivedAtUtc\" DESC, \"Id\" DESC);",
                suppressTransaction: true);

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryDailySummaries_BucketStartUtc",
                table: "TelemetryDailySummaries",
                column: "BucketStartUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryHourlySummaries_BucketStartUtc",
                table: "TelemetryHourlySummaries",
                column: "BucketStartUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TelemetryDailySummaries");

            migrationBuilder.DropTable(
                name: "TelemetryHourlySummaries");

            migrationBuilder.DropTable(
                name: "TelemetryMaintenanceStates");

            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_TelemetryReadings_DeviceId_ReceivedAtUtc_Id\";",
                suppressTransaction: true);
        }
    }
}
