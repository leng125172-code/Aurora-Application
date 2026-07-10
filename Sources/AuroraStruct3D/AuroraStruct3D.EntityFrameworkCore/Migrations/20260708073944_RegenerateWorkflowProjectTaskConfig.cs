using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class RegenerateWorkflowProjectTaskConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CycleIntervalSeconds",
                table: "AbpProWorkflowProjectTasks");

            migrationBuilder.DropColumn(
                name: "TaskType",
                table: "AbpProWorkflowProjectTasks");

            migrationBuilder.CreateTable(
                name: "AbpProWorkflowProjectTaskConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskType = table.Column<int>(type: "integer", nullable: false),
                    CycleIntervalSeconds = table.Column<int>(type: "integer", nullable: true),
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
                    table.PrimaryKey("PK_AbpProWorkflowProjectTaskConfigs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProWorkflowProjectTaskConfigs_ProjectId",
                table: "AbpProWorkflowProjectTaskConfigs",
                column: "ProjectId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AbpProWorkflowProjectTaskConfigs");

            migrationBuilder.AddColumn<int>(
                name: "CycleIntervalSeconds",
                table: "AbpProWorkflowProjectTasks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TaskType",
                table: "AbpProWorkflowProjectTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
