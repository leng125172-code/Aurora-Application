using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRuntimeVariablePoolColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RuntimeInstanceId",
                table: "AbpProWorkflowProjectTasks");

            migrationBuilder.DropColumn(
                name: "UseOnlineVariablePool",
                table: "AbpProWorkflowProjectTasks");

            migrationBuilder.DropColumn(
                name: "VariableReadTimeoutMs",
                table: "AbpProWorkflowProjectTasks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RuntimeInstanceId",
                table: "AbpProWorkflowProjectTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UseOnlineVariablePool",
                table: "AbpProWorkflowProjectTasks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "VariableReadTimeoutMs",
                table: "AbpProWorkflowProjectTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
