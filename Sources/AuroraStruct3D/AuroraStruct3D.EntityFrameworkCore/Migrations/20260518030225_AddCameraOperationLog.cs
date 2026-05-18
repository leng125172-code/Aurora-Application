using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddCameraOperationLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AbpProCameraOperationLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CameraDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceIndex = table.Column<int>(type: "integer", nullable: false),
                    OperationType = table.Column<int>(type: "integer", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsSuccess = table.Column<bool>(type: "boolean", nullable: false),
                    ParameterSummary = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    RoundTripMs = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProCameraOperationLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbpProCameraOperationLogs_AbpProCameraDevices_CameraDeviceId",
                        column: x => x.CameraDeviceId,
                        principalTable: "AbpProCameraDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCameraOperationLogs_CameraDeviceId",
                table: "AbpProCameraOperationLogs",
                column: "CameraDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCameraOperationLogs_CameraDeviceId_IsSuccess",
                table: "AbpProCameraOperationLogs",
                columns: new[] { "CameraDeviceId", "IsSuccess" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCameraOperationLogs_CameraDeviceId_OccurredAt",
                table: "AbpProCameraOperationLogs",
                columns: new[] { "CameraDeviceId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCameraOperationLogs_CameraDeviceId_OperationType",
                table: "AbpProCameraOperationLogs",
                columns: new[] { "CameraDeviceId", "OperationType" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCameraOperationLogs_OccurredAt",
                table: "AbpProCameraOperationLogs",
                column: "OccurredAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AbpProCameraOperationLogs");
        }
    }
}
