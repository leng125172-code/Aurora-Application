using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowProjectTask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AbpProWorkflowProjectTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    HangfireJobId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ContinueOnError = table.Column<bool>(type: "boolean", nullable: false),
                    UseOnlineVariablePool = table.Column<bool>(type: "boolean", nullable: false),
                    RuntimeInstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    VariableReadTimeoutMs = table.Column<int>(type: "integer", nullable: false),
                    WorkflowIdsJson = table.Column<string>(type: "character varying(16384)", maxLength: 16384, nullable: false),
                    ResultsJson = table.Column<string>(type: "character varying(131072)", maxLength: 131072, nullable: false),
                    ExecutedCount = table.Column<int>(type: "integer", nullable: false),
                    SuccessCount = table.Column<int>(type: "integer", nullable: false),
                    FailedCount = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    IsCancelRequested = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_AbpProWorkflowProjectTasks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProWorkflowProjectTasks_HangfireJobId",
                table: "AbpProWorkflowProjectTasks",
                column: "HangfireJobId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProWorkflowProjectTasks_ProjectId_CreationTime",
                table: "AbpProWorkflowProjectTasks",
                columns: new[] { "ProjectId", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProWorkflowProjectTasks_Status",
                table: "AbpProWorkflowProjectTasks",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AbpProWorkflowProjectTasks");
        }
    }
}
