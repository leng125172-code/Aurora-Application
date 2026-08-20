using AuroraStruct3D.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.Migrations;

[DbContext(typeof(AuroraStruct3DDbContext))]
[Migration("20260803083000_MakeProjectTasksMultipleWithHandshake")]
public partial class MakeProjectTasksMultipleWithHandshake : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_AbpProWorkflowProjectTaskConfigs_ProjectId",
            table: "AbpProWorkflowProjectTaskConfigs");
        migrationBuilder.DropIndex(
            name: "IX_AbpProWorkflowPlcHandshakeConfigs_ProjectId",
            table: "AbpProWorkflowPlcHandshakeConfigs");
        migrationBuilder.DropIndex(
            name: "IX_AbpProWorkflowPlcHandshakeConfigs_PlcDeviceId",
            table: "AbpProWorkflowPlcHandshakeConfigs");

        migrationBuilder.AddColumn<Guid>(
            name: "TaskConfigId",
            table: "AbpProWorkflowPlcHandshakeConfigs",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.Sql("""
            UPDATE "AbpProWorkflowPlcHandshakeConfigs" h
            SET "TaskConfigId" = t."Id"
            FROM "AbpProWorkflowProjectTaskConfigs" t
            WHERE t."ProjectId" = h."ProjectId" AND t."IsDeleted" = false;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_AbpProWorkflowProjectTaskConfigs_ProjectId",
            table: "AbpProWorkflowProjectTaskConfigs",
            column: "ProjectId");
        migrationBuilder.CreateIndex(
            name: "IX_AbpProWorkflowProjectTaskConfigs_ProjectId_IsEnabled",
            table: "AbpProWorkflowProjectTaskConfigs",
            columns: new[] { "ProjectId", "IsEnabled" },
            unique: true,
            filter: "\"IsEnabled\" = true AND \"IsDeleted\" = false");
        migrationBuilder.CreateIndex(
            name: "IX_AbpProWorkflowPlcHandshakeConfigs_ProjectId",
            table: "AbpProWorkflowPlcHandshakeConfigs",
            column: "ProjectId");
        migrationBuilder.CreateIndex(
            name: "IX_AbpProWorkflowPlcHandshakeConfigs_PlcDeviceId",
            table: "AbpProWorkflowPlcHandshakeConfigs",
            column: "PlcDeviceId");
        migrationBuilder.CreateIndex(
            name: "IX_AbpProWorkflowPlcHandshakeConfigs_TaskConfigId",
            table: "AbpProWorkflowPlcHandshakeConfigs",
            column: "TaskConfigId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_AbpProWorkflowPlcHandshakeConfigs_PlcDeviceId",
            "AbpProWorkflowPlcHandshakeConfigs");
        migrationBuilder.DropIndex("IX_AbpProWorkflowProjectTaskConfigs_ProjectId_IsEnabled",
            "AbpProWorkflowProjectTaskConfigs");
        migrationBuilder.DropIndex("IX_AbpProWorkflowPlcHandshakeConfigs_TaskConfigId",
            "AbpProWorkflowPlcHandshakeConfigs");
        migrationBuilder.DropColumn("TaskConfigId", "AbpProWorkflowPlcHandshakeConfigs");
        migrationBuilder.DropIndex("IX_AbpProWorkflowProjectTaskConfigs_ProjectId",
            "AbpProWorkflowProjectTaskConfigs");
        migrationBuilder.DropIndex("IX_AbpProWorkflowPlcHandshakeConfigs_ProjectId",
            "AbpProWorkflowPlcHandshakeConfigs");
        migrationBuilder.CreateIndex("IX_AbpProWorkflowProjectTaskConfigs_ProjectId",
            "AbpProWorkflowProjectTaskConfigs", "ProjectId", unique: true);
        migrationBuilder.CreateIndex("IX_AbpProWorkflowPlcHandshakeConfigs_ProjectId",
            "AbpProWorkflowPlcHandshakeConfigs", "ProjectId", unique: true);
        migrationBuilder.CreateIndex("IX_AbpProWorkflowPlcHandshakeConfigs_PlcDeviceId",
            "AbpProWorkflowPlcHandshakeConfigs", "PlcDeviceId", unique: true,
            filter: "\"IsEnabled\" = true AND \"IsDeleted\" = false");
    }
}
