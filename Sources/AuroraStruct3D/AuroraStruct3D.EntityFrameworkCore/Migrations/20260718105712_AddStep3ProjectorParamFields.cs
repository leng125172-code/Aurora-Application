using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddStep3ProjectorParamFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FringeType",
                table: "AbpProCalibProjectorParams",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "bw");

            migrationBuilder.AddColumn<int>(
                name: "PeriodCount",
                table: "AbpProCalibProjectorParams",
                type: "integer",
                nullable: false,
                defaultValue: 8);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FringeType",
                table: "AbpProCalibProjectorParams");

            migrationBuilder.DropColumn(
                name: "PeriodCount",
                table: "AbpProCalibProjectorParams");
        }
    }
}
