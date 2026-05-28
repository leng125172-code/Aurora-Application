using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class Add_KtechMotorFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KtechChipId",
                table: "AbpProMotorAxes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "KtechDeviceTypeCode",
                table: "AbpProMotorAxes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KtechDriverName",
                table: "AbpProMotorAxes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KtechFirmwareVersion",
                table: "AbpProMotorAxes",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KtechHardwareVersion",
                table: "AbpProMotorAxes",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KtechMotorName",
                table: "AbpProMotorAxes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KtechMotorVersion",
                table: "AbpProMotorAxes",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KtechChipId",
                table: "AbpProMotorAxes");

            migrationBuilder.DropColumn(
                name: "KtechDeviceTypeCode",
                table: "AbpProMotorAxes");

            migrationBuilder.DropColumn(
                name: "KtechDriverName",
                table: "AbpProMotorAxes");

            migrationBuilder.DropColumn(
                name: "KtechFirmwareVersion",
                table: "AbpProMotorAxes");

            migrationBuilder.DropColumn(
                name: "KtechHardwareVersion",
                table: "AbpProMotorAxes");

            migrationBuilder.DropColumn(
                name: "KtechMotorName",
                table: "AbpProMotorAxes");

            migrationBuilder.DropColumn(
                name: "KtechMotorVersion",
                table: "AbpProMotorAxes");
        }
    }
}
