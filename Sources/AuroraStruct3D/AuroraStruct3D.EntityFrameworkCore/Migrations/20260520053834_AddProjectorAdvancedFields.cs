using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectorAdvancedFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BootImage",
                table: "AbpProProjectors",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "CheckerboardPixelSize",
                table: "AbpProProjectors",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "FlipMode",
                table: "AbpProProjectors",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LastColor",
                table: "AbpProProjectors",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<byte>(
                name: "LedRgbB",
                table: "AbpProProjectors",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)75);

            migrationBuilder.AddColumn<byte>(
                name: "LedRgbG",
                table: "AbpProProjectors",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)75);

            migrationBuilder.AddColumn<byte>(
                name: "LedRgbR",
                table: "AbpProProjectors",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)75);

            migrationBuilder.AddColumn<int>(
                name: "TriggerMode",
                table: "AbpProProjectors",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BootImage",
                table: "AbpProProjectors");

            migrationBuilder.DropColumn(
                name: "CheckerboardPixelSize",
                table: "AbpProProjectors");

            migrationBuilder.DropColumn(
                name: "FlipMode",
                table: "AbpProProjectors");

            migrationBuilder.DropColumn(
                name: "LastColor",
                table: "AbpProProjectors");

            migrationBuilder.DropColumn(
                name: "LedRgbB",
                table: "AbpProProjectors");

            migrationBuilder.DropColumn(
                name: "LedRgbG",
                table: "AbpProProjectors");

            migrationBuilder.DropColumn(
                name: "LedRgbR",
                table: "AbpProProjectors");

            migrationBuilder.DropColumn(
                name: "TriggerMode",
                table: "AbpProProjectors");
        }
    }
}
