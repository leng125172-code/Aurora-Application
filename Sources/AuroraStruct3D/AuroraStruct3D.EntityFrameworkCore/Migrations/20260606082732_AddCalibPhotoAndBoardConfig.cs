using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddCalibPhotoAndBoardConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PhysicalCornerCols",
                table: "AbpProCalibProjects",
                type: "integer",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<int>(
                name: "PhysicalCornerRows",
                table: "AbpProCalibProjects",
                type: "integer",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<decimal>(
                name: "PhysicalSquareSizeMm",
                table: "AbpProCalibProjects",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: false,
                defaultValue: 0m
            );

            migrationBuilder.AddColumn<int>(
                name: "ProjectedCornerCols",
                table: "AbpProCalibProjects",
                type: "integer",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<int>(
                name: "ProjectedCornerRows",
                table: "AbpProCalibProjects",
                type: "integer",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<int>(
                name: "ProjectedPixelSize",
                table: "AbpProCalibProjects",
                type: "integer",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<string>(
                name: "DistCoeffsJson",
                table: "AbpProCalibCameraParams",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "ExtrinsicRvecJson",
                table: "AbpProCalibCameraParams",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "ExtrinsicTvecJson",
                table: "AbpProCalibCameraParams",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "IntrinsicMatrixJson",
                table: "AbpProCalibCameraParams",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true
            );

            migrationBuilder.AddColumn<double>(
                name: "ReprojectionError",
                table: "AbpProCalibCameraParams",
                type: "double precision",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "AbpProCalibPhotoRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalibProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CameraDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PhotoType = table.Column<int>(type: "integer", nullable: false),
                    BlobKey = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: false
                    ),
                    ThumbnailBase64 = table.Column<string>(type: "text", nullable: true),
                    IsValid = table.Column<bool>(type: "boolean", nullable: false),
                    CornerCountDetected = table.Column<int>(type: "integer", nullable: false),
                    CapturedAt = table.Column<DateTime>(
                        type: "timestamp without time zone",
                        nullable: false
                    ),
                    CreationTime = table.Column<DateTime>(
                        type: "timestamp without time zone",
                        nullable: false
                    ),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(
                        type: "timestamp without time zone",
                        nullable: true
                    ),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProCalibPhotoRecords", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibPhotoRecords_CalibProjectId",
                table: "AbpProCalibPhotoRecords",
                column: "CalibProjectId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibPhotoRecords_CalibProjectId_CameraDeviceId",
                table: "AbpProCalibPhotoRecords",
                columns: new[] { "CalibProjectId", "CameraDeviceId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibPhotoRecords_CalibProjectId_CameraDeviceId_Photo~",
                table: "AbpProCalibPhotoRecords",
                columns: new[] { "CalibProjectId", "CameraDeviceId", "PhotoType" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibPhotoRecords_CapturedAt",
                table: "AbpProCalibPhotoRecords",
                column: "CapturedAt"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AbpProCalibPhotoRecords");

            migrationBuilder.DropColumn(name: "PhysicalCornerCols", table: "AbpProCalibProjects");

            migrationBuilder.DropColumn(name: "PhysicalCornerRows", table: "AbpProCalibProjects");

            migrationBuilder.DropColumn(name: "PhysicalSquareSizeMm", table: "AbpProCalibProjects");

            migrationBuilder.DropColumn(name: "ProjectedCornerCols", table: "AbpProCalibProjects");

            migrationBuilder.DropColumn(name: "ProjectedCornerRows", table: "AbpProCalibProjects");

            migrationBuilder.DropColumn(name: "ProjectedPixelSize", table: "AbpProCalibProjects");

            migrationBuilder.DropColumn(name: "DistCoeffsJson", table: "AbpProCalibCameraParams");

            migrationBuilder.DropColumn(
                name: "ExtrinsicRvecJson",
                table: "AbpProCalibCameraParams"
            );

            migrationBuilder.DropColumn(
                name: "ExtrinsicTvecJson",
                table: "AbpProCalibCameraParams"
            );

            migrationBuilder.DropColumn(
                name: "IntrinsicMatrixJson",
                table: "AbpProCalibCameraParams"
            );

            migrationBuilder.DropColumn(
                name: "ReprojectionError",
                table: "AbpProCalibCameraParams"
            );
        }
    }
}
