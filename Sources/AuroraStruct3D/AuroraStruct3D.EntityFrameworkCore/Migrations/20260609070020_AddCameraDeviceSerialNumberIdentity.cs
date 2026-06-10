using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddCameraDeviceSerialNumberIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeviceSerialNumber",
                table: "AbpProCameraDevices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCameraDevices_DeviceSerialNumber",
                table: "AbpProCameraDevices",
                column: "DeviceSerialNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AbpProCameraDevices_DeviceSerialNumber",
                table: "AbpProCameraDevices");

            migrationBuilder.DropColumn(
                name: "DeviceSerialNumber",
                table: "AbpProCameraDevices");
        }
    }
}
