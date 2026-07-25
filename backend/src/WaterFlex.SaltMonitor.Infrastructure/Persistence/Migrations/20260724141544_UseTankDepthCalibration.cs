using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UseTankDepthCalibration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "EmptyDistanceMm",
                table: "TankCalibrations",
                newName: "TankDepthMm");

            migrationBuilder.RenameColumn(
                name: "FullDistanceMm",
                table: "TankCalibrations",
                newName: "CommissioningDistanceMm");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TankDepthMm",
                table: "TankCalibrations",
                newName: "EmptyDistanceMm");

            migrationBuilder.RenameColumn(
                name: "CommissioningDistanceMm",
                table: "TankCalibrations",
                newName: "FullDistanceMm");
        }
    }
}
