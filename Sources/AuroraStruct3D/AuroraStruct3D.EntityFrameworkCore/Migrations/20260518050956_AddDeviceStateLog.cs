using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceStateLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AbpProDeviceStateLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    PreviousStatus = table.Column<int>(type: "integer", nullable: true),
                    NewStatus = table.Column<int>(type: "integer", nullable: false),
                    IsStatusChange = table.Column<bool>(type: "boolean", nullable: false),
                    PreviousMode = table.Column<int>(type: "integer", nullable: true),
                    NewMode = table.Column<int>(type: "integer", nullable: false),
                    IsModeChange = table.Column<bool>(type: "boolean", nullable: false),
                    Trigger = table.Column<int>(type: "integer", nullable: false),
                    FaultLevel = table.Column<int>(type: "integer", nullable: true),
                    FaultCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OperatorId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    OperatorName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Remark = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProDeviceStateLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProDeviceStateLogs_IsModeChange",
                table: "AbpProDeviceStateLogs",
                column: "IsModeChange");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProDeviceStateLogs_IsStatusChange",
                table: "AbpProDeviceStateLogs",
                column: "IsStatusChange");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProDeviceStateLogs_NewStatus",
                table: "AbpProDeviceStateLogs",
                column: "NewStatus");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProDeviceStateLogs_OccurredAt",
                table: "AbpProDeviceStateLogs",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProDeviceStateLogs_Trigger",
                table: "AbpProDeviceStateLogs",
                column: "Trigger");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AbpProDeviceStateLogs");
        }
    }
}
