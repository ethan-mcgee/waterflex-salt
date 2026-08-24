using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceHealthAndOperationalTelemetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LastClockSynchronized",
                table: "Devices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastDeviceReportedAtUtc",
                table: "Devices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastHealthFirmwareVersion",
                table: "Devices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastHealthReportedAtUtc",
                table: "Devices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastHealthWifiRssiDbm",
                table: "Devices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastQueuedReadingCount",
                table: "Devices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LastSensorFault",
                table: "Devices",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastSensorStatus",
                table: "Devices",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unknown");

            // Rebuild every bucket whose raw inputs still exist. For older
            // buckets whose raw rows have already expired, preserve only
            // summaries that meet the operational-quality contract.
            migrationBuilder.Sql("""
                DELETE FROM "TelemetryDailySummaries" daily
                WHERE daily."ErrorCount" > 0
                   OR daily."WorstQuality" < 70
                   OR EXISTS (
                       SELECT 1 FROM "TelemetryReadings" raw
                       WHERE raw."DeviceId" = daily."DeviceId"
                         AND date_trunc('day', raw."ReceivedAtUtc" AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'
                             = daily."BucketStartUtc");

                DELETE FROM "TelemetryHourlySummaries" hourly
                WHERE hourly."ErrorCount" > 0
                   OR hourly."WorstQuality" < 70
                   OR EXISTS (
                       SELECT 1 FROM "TelemetryReadings" raw
                       WHERE raw."DeviceId" = hourly."DeviceId"
                         AND date_trunc('hour', raw."ReceivedAtUtc" AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'
                             = hourly."BucketStartUtc");

                DELETE FROM "TelemetryMaintenanceStates"
                WHERE "Name" LIKE 'telemetry-history-backfill-%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastClockSynchronized",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "LastDeviceReportedAtUtc",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "LastHealthFirmwareVersion",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "LastHealthReportedAtUtc",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "LastHealthWifiRssiDbm",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "LastQueuedReadingCount",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "LastSensorFault",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "LastSensorStatus",
                table: "Devices");
        }
    }
}
