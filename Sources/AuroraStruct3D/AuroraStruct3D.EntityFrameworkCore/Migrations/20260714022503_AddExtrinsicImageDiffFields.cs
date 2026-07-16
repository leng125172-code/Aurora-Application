using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddExtrinsicImageDiffFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "ImageDiffScore",
                table: "AbpProCalibPhotoRecords",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ImageDiffSignificant",
                table: "AbpProCalibPhotoRecords",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageDiffScore",
                table: "AbpProCalibPhotoRecords");

            migrationBuilder.DropColumn(
                name: "ImageDiffSignificant",
                table: "AbpProCalibPhotoRecords");
        }
    }
}
