using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    [DbContext(typeof(AuroraStruct3DDbContext))]
    [Migration("20260820100000_AddIntrinsicPhotoQualityScores")]
    public partial class AddIntrinsicPhotoQualityScores : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExposureScore",
                table: "AbpProCalibPhotoRecords",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SharpnessScore",
                table: "AbpProCalibPhotoRecords",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AbpProCalibPhotoRecords_ExposureScore",
                table: "AbpProCalibPhotoRecords",
                sql: "\"ExposureScore\" IS NULL OR (\"ExposureScore\" >= 1 AND \"ExposureScore\" <= 100)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AbpProCalibPhotoRecords_SharpnessScore",
                table: "AbpProCalibPhotoRecords",
                sql: "\"SharpnessScore\" IS NULL OR (\"SharpnessScore\" >= 1 AND \"SharpnessScore\" <= 100)");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AbpProCalibPhotoRecords_ExposureScore",
                table: "AbpProCalibPhotoRecords");
            migrationBuilder.DropCheckConstraint(
                name: "CK_AbpProCalibPhotoRecords_SharpnessScore",
                table: "AbpProCalibPhotoRecords");
            migrationBuilder.DropColumn(name: "ExposureScore", table: "AbpProCalibPhotoRecords");
            migrationBuilder.DropColumn(name: "SharpnessScore", table: "AbpProCalibPhotoRecords");
        }
    }
}
