using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddMotorRotationAngleRange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "MaxRotationAngle",
                table: "AbpProMotorAxes",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MinRotationAngle",
                table: "AbpProMotorAxes",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxRotationAngle",
                table: "AbpProMotorAxes");

            migrationBuilder.DropColumn(
                name: "MinRotationAngle",
                table: "AbpProMotorAxes");
        }
    }
}
