using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    [DbContext(typeof(AuroraStruct3DDbContext))]
    [Migration("20260824150000_AddProductModelUnitAndSampling")]
    public partial class AddProductModelUnitAndSampling : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LengthUnit",
                table: "AbpProProductModels",
                type: "integer",
                nullable: false,
                defaultValue: 0
            );
            migrationBuilder.AddColumn<double>(
                name: "SurfaceSamplingSpacingMm",
                table: "AbpProProductModels",
                type: "double precision",
                nullable: false,
                defaultValue: 0.5d
            );
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "LengthUnit", table: "AbpProProductModels");
            migrationBuilder.DropColumn(
                name: "SurfaceSamplingSpacingMm",
                table: "AbpProProductModels"
            );
        }
    }
}
