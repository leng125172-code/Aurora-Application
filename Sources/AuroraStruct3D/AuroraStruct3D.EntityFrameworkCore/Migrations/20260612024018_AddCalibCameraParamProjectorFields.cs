using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddCalibCameraParamProjectorFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CameraToProjectorRJson",
                table: "AbpProCalibCameraParams",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CameraToProjectorTJson",
                table: "AbpProCalibCameraParams",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ProjectorCalibReprojectionError",
                table: "AbpProCalibCameraParams",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProjectorDistCoeffsJson",
                table: "AbpProCalibCameraParams",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProjectorIntrinsicMatrixJson",
                table: "AbpProCalibCameraParams",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CameraToProjectorRJson",
                table: "AbpProCalibCameraParams");

            migrationBuilder.DropColumn(
                name: "CameraToProjectorTJson",
                table: "AbpProCalibCameraParams");

            migrationBuilder.DropColumn(
                name: "ProjectorCalibReprojectionError",
                table: "AbpProCalibCameraParams");

            migrationBuilder.DropColumn(
                name: "ProjectorDistCoeffsJson",
                table: "AbpProCalibCameraParams");

            migrationBuilder.DropColumn(
                name: "ProjectorIntrinsicMatrixJson",
                table: "AbpProCalibCameraParams");
        }
    }
}
