using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WaterFlex.SaltMonitor.Infrastructure.Persistence;

#nullable disable

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence.Migrations;

/// <summary>
/// Normalizes health values emitted by the retired sensor firmware before the
/// corresponding enum members are removed from the application contract.
/// </summary>
[DbContext(typeof(SaltMonitorDbContext))]
[Migration("20260818190000_NormalizeLegacySensorFaults")]
public sealed class NormalizeLegacySensorFaults : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE "Devices"
            SET "LastSensorFault" = 'InvalidSignal'
            WHERE "LastSensorFault" IN ('StuckHigh', 'StuckLow');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The two legacy values cannot be reconstructed after normalization.
    }
}

