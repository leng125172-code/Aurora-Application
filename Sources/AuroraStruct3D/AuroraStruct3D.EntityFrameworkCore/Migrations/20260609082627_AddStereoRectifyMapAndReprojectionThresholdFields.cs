using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddStereoRectifyMapAndReprojectionThresholdFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PairGroupId",
                table: "AbpProCalibPhotoRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StereoRole",
                table: "AbpProCalibPhotoRecords",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ProjectorReprojectionError",
                table: "AbpProCalibCameraParams",
                type: "double precision",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AbpProCalibStereoResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalibProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    MainCameraDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SecondaryCameraDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    StereoReprojectionError = table.Column<double>(type: "double precision", nullable: false),
                    RotationMatrixJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    TranslationVectorJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    TransformLtoRJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    TransformRtoLJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    RectificationR1Json = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    RectificationR2Json = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ProjectionP1Json = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ProjectionP2Json = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    RectifyMapWidth = table.Column<int>(type: "integer", nullable: false),
                    RectifyMapHeight = table.Column<int>(type: "integer", nullable: false),
                    Map1XBlobKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Map1YBlobKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Map2XBlobKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Map2YBlobKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProCalibStereoResults", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibPhotoRecords_CalibProjectId_PairGroupId",
                table: "AbpProCalibPhotoRecords",
                columns: new[] { "CalibProjectId", "PairGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibStereoResults_CalibProjectId",
                table: "AbpProCalibStereoResults",
                column: "CalibProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibStereoResults_MainCameraDeviceId_SecondaryCamera~",
                table: "AbpProCalibStereoResults",
                columns: new[] { "MainCameraDeviceId", "SecondaryCameraDeviceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AbpProCalibStereoResults");

            migrationBuilder.DropIndex(
                name: "IX_AbpProCalibPhotoRecords_CalibProjectId_PairGroupId",
                table: "AbpProCalibPhotoRecords");

            migrationBuilder.DropColumn(
                name: "PairGroupId",
                table: "AbpProCalibPhotoRecords");

            migrationBuilder.DropColumn(
                name: "StereoRole",
                table: "AbpProCalibPhotoRecords");

            migrationBuilder.DropColumn(
                name: "ProjectorReprojectionError",
                table: "AbpProCalibCameraParams");
        }
    }
}
