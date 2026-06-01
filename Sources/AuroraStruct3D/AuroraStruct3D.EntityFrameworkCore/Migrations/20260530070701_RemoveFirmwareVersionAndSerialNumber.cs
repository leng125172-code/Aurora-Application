using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class RemoveFirmwareVersionAndSerialNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FirmwareVersion",
                table: "AbpProProjectors");

            migrationBuilder.DropColumn(
                name: "SerialNumber",
                table: "AbpProCameraDevices");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FirmwareVersion",
                table: "AbpProProjectors",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SerialNumber",
                table: "AbpProCameraDevices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }
    }
}
