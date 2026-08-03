using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowPlcHandshake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResultVariableName",
                table: "AbpProWorkflowProjectTaskConfigs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResultWorkflowId",
                table: "AbpProWorkflowProjectTaskConfigs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InspectionDecision",
                table: "AbpProWorkflowProjectRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "InspectionErrorCode",
                table: "AbpProWorkflowProjectRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "InspectionErrorMessage",
                table: "AbpProWorkflowProjectRuns",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AbpProWorkflowPlcHandshakeConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlcDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CaptureRequestTagId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestIdTagId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResultAckTagId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResultAckIdTagId = table.Column<Guid>(type: "uuid", nullable: false),
                    HeartbeatTagId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceStatusTagId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskStatusTagId = table.Column<Guid>(type: "uuid", nullable: false),
                    CanCaptureTagId = table.Column<Guid>(type: "uuid", nullable: false),
                    CaptureAckTagId = table.Column<Guid>(type: "uuid", nullable: false),
                    AckRequestIdTagId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResultValidTagId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResultRequestIdTagId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResultCodeTagId = table.Column<Guid>(type: "uuid", nullable: false),
                    ErrorCodeTagId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Phase = table.Column<int>(type: "integer", nullable: false),
                    CurrentRequestId = table.Column<int>(type: "integer", nullable: false),
                    LastCompletedRequestId = table.Column<int>(type: "integer", nullable: false),
                    CurrentRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResultCode = table.Column<int>(type: "integer", nullable: false),
                    ErrorCode = table.Column<int>(type: "integer", nullable: false),
                    LastRequestAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastResultAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
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
                    table.PrimaryKey("PK_AbpProWorkflowPlcHandshakeConfigs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProWorkflowPlcHandshakeConfigs_CurrentRunId",
                table: "AbpProWorkflowPlcHandshakeConfigs",
                column: "CurrentRunId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProWorkflowPlcHandshakeConfigs_PlcDeviceId",
                table: "AbpProWorkflowPlcHandshakeConfigs",
                column: "PlcDeviceId",
                unique: true,
                filter: "\"IsEnabled\" = true AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProWorkflowPlcHandshakeConfigs_ProjectId",
                table: "AbpProWorkflowPlcHandshakeConfigs",
                column: "ProjectId",
                unique: true);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AbpProWorkflowPlcHandshakeConfigs");

            migrationBuilder.DropColumn(
                name: "ResultVariableName",
                table: "AbpProWorkflowProjectTaskConfigs");

            migrationBuilder.DropColumn(
                name: "ResultWorkflowId",
                table: "AbpProWorkflowProjectTaskConfigs");

            migrationBuilder.DropColumn(
                name: "InspectionDecision",
                table: "AbpProWorkflowProjectRuns");

            migrationBuilder.DropColumn(
                name: "InspectionErrorCode",
                table: "AbpProWorkflowProjectRuns");

            migrationBuilder.DropColumn(
                name: "InspectionErrorMessage",
                table: "AbpProWorkflowProjectRuns");

        }
    }
}
