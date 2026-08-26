using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    [DbContext(typeof(AuroraStruct3DDbContext))]
    [Migration("20260824090000_AddProjectorGrayLevels")]
    public partial class AddProjectorGrayLevels : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "DarkLevel", table: "AbpProCalibProjectorParams",
                type: "smallint", nullable: false, defaultValue: (byte)24);
            migrationBuilder.AddColumn<byte>(
                name: "BrightLevel", table: "AbpProCalibProjectorParams",
                type: "smallint", nullable: false, defaultValue: (byte)220);
            migrationBuilder.AddCheckConstraint(
                name: "CK_AbpProCalibProjectorParams_GrayLevels",
                table: "AbpProCalibProjectorParams",
                sql: "\"DarkLevel\" >= 0 AND \"BrightLevel\" <= 255 AND \"DarkLevel\" < \"BrightLevel\"");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AbpProCalibProjectorParams_GrayLevels",
                table: "AbpProCalibProjectorParams");
            migrationBuilder.DropColumn(name: "DarkLevel", table: "AbpProCalibProjectorParams");
            migrationBuilder.DropColumn(name: "BrightLevel", table: "AbpProCalibProjectorParams");
        }
    }
}
