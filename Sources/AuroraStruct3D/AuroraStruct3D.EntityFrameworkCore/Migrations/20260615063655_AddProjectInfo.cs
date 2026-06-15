using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 注意：AbpProCalibCameraParams 中的以下列已在前一次迁移中添加，此处跳过：
            // CameraToProjectorRJson、CameraToProjectorTJson、ProjectorCalibReprojectionError、
            // ProjectorDistCoeffsJson、ProjectorIntrinsicMatrixJson

            migrationBuilder.CreateTable(
                name: "AbpProProjectInfos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectCode = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    Name = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: false
                    ),
                    Version = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    Description = table.Column<string>(
                        type: "character varying(2000)",
                        maxLength: 2000,
                        nullable: true
                    ),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(
                        type: "character varying(40)",
                        maxLength: 40,
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
                    IsDeleted = table.Column<bool>(
                        type: "boolean",
                        nullable: false,
                        defaultValue: false
                    ),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(
                        type: "timestamp without time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProProjectInfos", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProProjectInfos_CreationTime",
                table: "AbpProProjectInfos",
                column: "CreationTime"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProProjectInfos_CreatorId",
                table: "AbpProProjectInfos",
                column: "CreatorId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProProjectInfos_ProjectCode",
                table: "AbpProProjectInfos",
                column: "ProjectCode",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProProjectInfos_Status",
                table: "AbpProProjectInfos",
                column: "Status"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AbpProProjectInfos");

            // 注意：AbpProCalibCameraParams 中的相关列由前一次迁移管理，此处不做回滚：
            // CameraToProjectorRJson、CameraToProjectorTJson、ProjectorCalibReprojectionError、
            // ProjectorDistCoeffsJson、ProjectorIntrinsicMatrixJson
        }
    }
}
