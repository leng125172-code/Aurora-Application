using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class RemoveProjectorHidVidPid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AbpProProjectors_HidVendorId_HidProductId_HidDeviceIndex",
                table: "AbpProProjectors");

            migrationBuilder.DropColumn(
                name: "HidProductId",
                table: "AbpProProjectors");

            migrationBuilder.DropColumn(
                name: "HidVendorId",
                table: "AbpProProjectors");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProProjectors_HidDeviceIndex",
                table: "AbpProProjectors",
                column: "HidDeviceIndex",
                unique: true,
                filter: "\"ConnectionType\" = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AbpProProjectors_HidDeviceIndex",
                table: "AbpProProjectors");

            migrationBuilder.AddColumn<int>(
                name: "HidProductId",
                table: "AbpProProjectors",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "HidVendorId",
                table: "AbpProProjectors",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_AbpProProjectors_HidVendorId_HidProductId_HidDeviceIndex",
                table: "AbpProProjectors",
                columns: new[] { "HidVendorId", "HidProductId", "HidDeviceIndex" },
                unique: true,
                filter: "\"ConnectionType\" = 1");
        }
    }
}
