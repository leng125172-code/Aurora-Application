using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddRuntimeFaultHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DeviceId",
                table: "AbpProDeviceFaults",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeviceName",
                table: "AbpProDeviceFaults",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Fingerprint",
                table: "AbpProDeviceFaults",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastOccurredAt",
                table: "AbpProDeviceFaults",
                type: "timestamp without time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "OccurrenceCount",
                table: "AbpProDeviceFaults",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "AbpProDeviceFaults",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowId",
                table: "AbpProDeviceFaults",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkflowName",
                table: "AbpProDeviceFaults",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkflowNodeId",
                table: "AbpProDeviceFaults",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowProjectId",
                table: "AbpProDeviceFaults",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkflowProjectName",
                table: "AbpProDeviceFaults",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowRunId",
                table: "AbpProDeviceFaults",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbpProDeviceFaults_IsResolved_Fingerprint",
                table: "AbpProDeviceFaults",
                columns: new[] { "IsResolved", "Fingerprint" });

            migrationBuilder.Sql(
                "UPDATE \"AbpProDeviceFaults\" SET \"LastOccurredAt\" = \"OccurredAt\", \"OccurrenceCount\" = 1 WHERE \"OccurrenceCount\" = 0;"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AbpProDeviceFaults_IsResolved_Fingerprint",
                table: "AbpProDeviceFaults");

            migrationBuilder.DropColumn(
                name: "DeviceId",
                table: "AbpProDeviceFaults");

            migrationBuilder.DropColumn(
                name: "DeviceName",
                table: "AbpProDeviceFaults");

            migrationBuilder.DropColumn(
                name: "Fingerprint",
                table: "AbpProDeviceFaults");

            migrationBuilder.DropColumn(
                name: "LastOccurredAt",
                table: "AbpProDeviceFaults");

            migrationBuilder.DropColumn(
                name: "OccurrenceCount",
                table: "AbpProDeviceFaults");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "AbpProDeviceFaults");

            migrationBuilder.DropColumn(
                name: "WorkflowId",
                table: "AbpProDeviceFaults");

            migrationBuilder.DropColumn(
                name: "WorkflowName",
                table: "AbpProDeviceFaults");

            migrationBuilder.DropColumn(
                name: "WorkflowNodeId",
                table: "AbpProDeviceFaults");

            migrationBuilder.DropColumn(
                name: "WorkflowProjectId",
                table: "AbpProDeviceFaults");

            migrationBuilder.DropColumn(
                name: "WorkflowProjectName",
                table: "AbpProDeviceFaults");

            migrationBuilder.DropColumn(
                name: "WorkflowRunId",
                table: "AbpProDeviceFaults");
        }
    }
}
