using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowTaskDeploymentTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DeploymentId",
                table: "AbpProWorkflowProjectTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeploymentRevision",
                table: "AbpProWorkflowProjectTasks",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbpProWorkflowProjectTasks_DeploymentId",
                table: "AbpProWorkflowProjectTasks",
                column: "DeploymentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AbpProWorkflowProjectTasks_DeploymentId",
                table: "AbpProWorkflowProjectTasks");

            migrationBuilder.DropColumn(
                name: "DeploymentId",
                table: "AbpProWorkflowProjectTasks");

            migrationBuilder.DropColumn(
                name: "DeploymentRevision",
                table: "AbpProWorkflowProjectTasks");
        }
    }
}
