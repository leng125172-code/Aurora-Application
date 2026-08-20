using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class DecoupleWorkflowPlcHandshakeAddresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AckRequestIdAddress",
                table: "AbpProWorkflowPlcHandshakeConfigs",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CanCaptureAddress",
                table: "AbpProWorkflowPlcHandshakeConfigs",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CaptureAckAddress",
                table: "AbpProWorkflowPlcHandshakeConfigs",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CaptureRequestAddress",
                table: "AbpProWorkflowPlcHandshakeConfigs",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DeviceStatusAddress",
                table: "AbpProWorkflowPlcHandshakeConfigs",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ErrorCodeAddress",
                table: "AbpProWorkflowPlcHandshakeConfigs",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "HeartbeatAddress",
                table: "AbpProWorkflowPlcHandshakeConfigs",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RequestIdAddress",
                table: "AbpProWorkflowPlcHandshakeConfigs",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ResultAckAddress",
                table: "AbpProWorkflowPlcHandshakeConfigs",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ResultAckIdAddress",
                table: "AbpProWorkflowPlcHandshakeConfigs",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ResultCodeAddress",
                table: "AbpProWorkflowPlcHandshakeConfigs",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ResultRequestIdAddress",
                table: "AbpProWorkflowPlcHandshakeConfigs",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ResultValidAddress",
                table: "AbpProWorkflowPlcHandshakeConfigs",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TaskStatusAddress",
                table: "AbpProWorkflowPlcHandshakeConfigs",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            // Existing rows reference debug PlcTag IDs and cannot be converted safely to
            // independent NodeIds without user confirmation. Disable them until reconfigured.
            migrationBuilder.Sql(
                "UPDATE \"AbpProWorkflowPlcHandshakeConfigs\" SET \"IsEnabled\" = FALSE;"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AckRequestIdAddress",
                table: "AbpProWorkflowPlcHandshakeConfigs");

            migrationBuilder.DropColumn(
                name: "CanCaptureAddress",
                table: "AbpProWorkflowPlcHandshakeConfigs");

            migrationBuilder.DropColumn(
                name: "CaptureAckAddress",
                table: "AbpProWorkflowPlcHandshakeConfigs");

            migrationBuilder.DropColumn(
                name: "CaptureRequestAddress",
                table: "AbpProWorkflowPlcHandshakeConfigs");

            migrationBuilder.DropColumn(
                name: "DeviceStatusAddress",
                table: "AbpProWorkflowPlcHandshakeConfigs");

            migrationBuilder.DropColumn(
                name: "ErrorCodeAddress",
                table: "AbpProWorkflowPlcHandshakeConfigs");

            migrationBuilder.DropColumn(
                name: "HeartbeatAddress",
                table: "AbpProWorkflowPlcHandshakeConfigs");

            migrationBuilder.DropColumn(
                name: "RequestIdAddress",
                table: "AbpProWorkflowPlcHandshakeConfigs");

            migrationBuilder.DropColumn(
                name: "ResultAckAddress",
                table: "AbpProWorkflowPlcHandshakeConfigs");

            migrationBuilder.DropColumn(
                name: "ResultAckIdAddress",
                table: "AbpProWorkflowPlcHandshakeConfigs");

            migrationBuilder.DropColumn(
                name: "ResultCodeAddress",
                table: "AbpProWorkflowPlcHandshakeConfigs");

            migrationBuilder.DropColumn(
                name: "ResultRequestIdAddress",
                table: "AbpProWorkflowPlcHandshakeConfigs");

            migrationBuilder.DropColumn(
                name: "ResultValidAddress",
                table: "AbpProWorkflowPlcHandshakeConfigs");

            migrationBuilder.DropColumn(
                name: "TaskStatusAddress",
                table: "AbpProWorkflowPlcHandshakeConfigs");
        }
    }
}
