using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceFaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FaultCode",
                table: "AbpProDeviceStateLogs");

            migrationBuilder.DropColumn(
                name: "FaultLevel",
                table: "AbpProDeviceStateLogs");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreationTime",
                table: "AbpProDeviceStateLogs",
                type: "timestamp without time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "CreatorId",
                table: "AbpProDeviceStateLogs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeleterId",
                table: "AbpProDeviceStateLogs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletionTime",
                table: "AbpProDeviceStateLogs",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DurationMs",
                table: "AbpProDeviceStateLogs",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ErrorMessage",
                table: "AbpProDeviceStateLogs",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FaultId",
                table: "AbpProDeviceStateLogs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "AbpProDeviceStateLogs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSuccessful",
                table: "AbpProDeviceStateLogs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsTransitionState",
                table: "AbpProDeviceStateLogs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModificationTime",
                table: "AbpProDeviceStateLogs",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastModifierId",
                table: "AbpProDeviceStateLogs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AbpProDeviceFaults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    FaultLevel = table.Column<int>(type: "integer", nullable: false),
                    FaultCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FaultMessage = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    FaultReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IsResolved = table.Column<bool>(type: "boolean", nullable: false),
                    IsAutoRecovered = table.Column<bool>(type: "boolean", nullable: false),
                    ResolverId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    ResolverName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ResolutionDescription = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    CausedModeSwitch = table.Column<bool>(type: "boolean", nullable: false),
                    SwitchedToMode = table.Column<int>(type: "integer", nullable: true),
                    StateLogId = table.Column<Guid>(type: "uuid", nullable: true),
                    Remark = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
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
                    table.PrimaryKey("PK_AbpProDeviceFaults", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProDeviceStateLogs_FaultId",
                table: "AbpProDeviceStateLogs",
                column: "FaultId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProDeviceFaults_FaultLevel",
                table: "AbpProDeviceFaults",
                column: "FaultLevel");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProDeviceFaults_IsResolved",
                table: "AbpProDeviceFaults",
                column: "IsResolved");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProDeviceFaults_IsResolved_FaultLevel",
                table: "AbpProDeviceFaults",
                columns: new[] { "IsResolved", "FaultLevel" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProDeviceFaults_OccurredAt",
                table: "AbpProDeviceFaults",
                column: "OccurredAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AbpProDeviceFaults");

            migrationBuilder.DropIndex(
                name: "IX_AbpProDeviceStateLogs_FaultId",
                table: "AbpProDeviceStateLogs");

            migrationBuilder.DropColumn(
                name: "CreationTime",
                table: "AbpProDeviceStateLogs");

            migrationBuilder.DropColumn(
                name: "CreatorId",
                table: "AbpProDeviceStateLogs");

            migrationBuilder.DropColumn(
                name: "DeleterId",
                table: "AbpProDeviceStateLogs");

            migrationBuilder.DropColumn(
                name: "DeletionTime",
                table: "AbpProDeviceStateLogs");

            migrationBuilder.DropColumn(
                name: "DurationMs",
                table: "AbpProDeviceStateLogs");

            migrationBuilder.DropColumn(
                name: "ErrorMessage",
                table: "AbpProDeviceStateLogs");

            migrationBuilder.DropColumn(
                name: "FaultId",
                table: "AbpProDeviceStateLogs");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "AbpProDeviceStateLogs");

            migrationBuilder.DropColumn(
                name: "IsSuccessful",
                table: "AbpProDeviceStateLogs");

            migrationBuilder.DropColumn(
                name: "IsTransitionState",
                table: "AbpProDeviceStateLogs");

            migrationBuilder.DropColumn(
                name: "LastModificationTime",
                table: "AbpProDeviceStateLogs");

            migrationBuilder.DropColumn(
                name: "LastModifierId",
                table: "AbpProDeviceStateLogs");

            migrationBuilder.AddColumn<string>(
                name: "FaultCode",
                table: "AbpProDeviceStateLogs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FaultLevel",
                table: "AbpProDeviceStateLogs",
                type: "integer",
                nullable: true);
        }
    }
}
