using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class RemoveGimbalAndThreeCamera : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AbpProCalibDeviceBindings_AbpProCalibGimbalGroups_BoundGimb~",
                table: "AbpProCalibDeviceBindings");

            migrationBuilder.DropTable(
                name: "AbpProCalibGimbalBindings");

            migrationBuilder.DropTable(
                name: "AbpProCalibGimbalGroups");

            migrationBuilder.DropIndex(
                name: "IX_AbpProCalibDeviceBindings_BoundGimbalGroupId",
                table: "AbpProCalibDeviceBindings");

            migrationBuilder.DropColumn(
                name: "BoundGimbalGroupId",
                table: "AbpProCalibDeviceBindings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BoundGimbalGroupId",
                table: "AbpProCalibDeviceBindings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AbpProCalibGimbalGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Acceleration = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    AccelerationTime = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecelerationTime = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    MaxSpeed = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProCalibGimbalGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AbpProCalibGimbalBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AxisType = table.Column<int>(type: "integer", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    GimbalGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    MotorAxisId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProCalibGimbalBindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbpProCalibGimbalBindings_AbpProCalibGimbalGroups_GimbalGro~",
                        column: x => x.GimbalGroupId,
                        principalTable: "AbpProCalibGimbalGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibDeviceBindings_BoundGimbalGroupId",
                table: "AbpProCalibDeviceBindings",
                column: "BoundGimbalGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibGimbalBindings_GimbalGroupId",
                table: "AbpProCalibGimbalBindings",
                column: "GimbalGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibGimbalBindings_GimbalGroupId_AxisType",
                table: "AbpProCalibGimbalBindings",
                columns: new[] { "GimbalGroupId", "AxisType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibGimbalGroups_IsEnabled",
                table: "AbpProCalibGimbalGroups",
                column: "IsEnabled");

            migrationBuilder.AddForeignKey(
                name: "FK_AbpProCalibDeviceBindings_AbpProCalibGimbalGroups_BoundGimb~",
                table: "AbpProCalibDeviceBindings",
                column: "BoundGimbalGroupId",
                principalTable: "AbpProCalibGimbalGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
