using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowPlcTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AbpProWorkflowPlcTriggers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlcDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlcTagId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpectedValueJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Tolerance = table.Column<double>(type: "double precision", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LastTriggeredAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastSkipReason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
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
                    table.PrimaryKey("PK_AbpProWorkflowPlcTriggers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProWorkflowPlcTriggers_PlcDeviceId_PlcTagId_IsEnabled",
                table: "AbpProWorkflowPlcTriggers",
                columns: new[] { "PlcDeviceId", "PlcTagId", "IsEnabled" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProWorkflowPlcTriggers_ProjectId",
                table: "AbpProWorkflowPlcTriggers",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AbpProWorkflowPlcTriggers");
        }
    }
}
