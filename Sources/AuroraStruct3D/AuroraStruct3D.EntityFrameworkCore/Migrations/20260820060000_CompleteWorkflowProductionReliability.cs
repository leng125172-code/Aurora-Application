using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations;

[DbContext(typeof(AuroraStruct3DDbContext))]
[Migration("20260820060000_CompleteWorkflowProductionReliability")]
public partial class CompleteWorkflowProductionReliability : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>("TaskConfigId", "AbpProWorkflowProjectDeployments", "uuid", nullable: true);
        migrationBuilder.AddColumn<Guid>("TaskConfigId", "AbpProWorkflowProjectRuns", "uuid", nullable: true);
        migrationBuilder.AddColumn<Guid>("PlcHandshakeConfigId", "AbpProWorkflowProjectRuns", "uuid", nullable: true);
        migrationBuilder.AddColumn<int>("PlcRequestId", "AbpProWorkflowProjectRuns", "integer", nullable: true);
        migrationBuilder.AddColumn<long>("PlcRequestSequence", "AbpProWorkflowProjectRuns", "bigint", nullable: true);
        migrationBuilder.AddColumn<long>("RequestSequence", "AbpProWorkflowPlcHandshakeConfigs", "bigint", nullable: false, defaultValue: 0L);

        migrationBuilder.Sql("""
            UPDATE "AbpProWorkflowProjectDeployments"
            SET "TaskConfigId" = NULLIF("FrozenTaskConfigJson"::jsonb ->> 'TaskConfigId', '')::uuid
            WHERE "FrozenTaskConfigJson" IS NOT NULL
              AND ("FrozenTaskConfigJson"::jsonb ? 'TaskConfigId')
              AND NULLIF("FrozenTaskConfigJson"::jsonb ->> 'TaskConfigId', '') IS NOT NULL;

            UPDATE "AbpProWorkflowProjectRuns" r
            SET "TaskConfigId" = d."TaskConfigId"
            FROM "AbpProWorkflowProjectDeployments" d
            WHERE r."DeploymentId" = d."Id" AND d."TaskConfigId" IS NOT NULL;
            """);

        migrationBuilder.CreateIndex("IX_AbpProWorkflowProjectDeployments_ProjectId_TaskConfigId_Status",
            "AbpProWorkflowProjectDeployments", new[] { "ProjectId", "TaskConfigId", "Status" });
        migrationBuilder.CreateIndex("IX_AbpProWorkflowProjectRuns_TaskConfigId",
            "AbpProWorkflowProjectRuns", "TaskConfigId");
        migrationBuilder.CreateIndex("IX_AbpProWorkflowProjectRuns_PlcHandshakeConfigId",
            "AbpProWorkflowProjectRuns", "PlcHandshakeConfigId");
        migrationBuilder.CreateIndex("IX_AbpProWorkflowProjectRuns_PlcHandshakeConfigId_PlcRequestSequence",
            "AbpProWorkflowProjectRuns", new[] { "PlcHandshakeConfigId", "PlcRequestSequence" }, unique: true,
            filter: "\"PlcHandshakeConfigId\" IS NOT NULL AND \"PlcRequestSequence\" IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_AbpProWorkflowProjectDeployments_ProjectId_TaskConfigId_Status", "AbpProWorkflowProjectDeployments");
        migrationBuilder.DropIndex("IX_AbpProWorkflowProjectRuns_TaskConfigId", "AbpProWorkflowProjectRuns");
        migrationBuilder.DropIndex("IX_AbpProWorkflowProjectRuns_PlcHandshakeConfigId", "AbpProWorkflowProjectRuns");
        migrationBuilder.DropIndex("IX_AbpProWorkflowProjectRuns_PlcHandshakeConfigId_PlcRequestSequence", "AbpProWorkflowProjectRuns");
        migrationBuilder.DropColumn("TaskConfigId", "AbpProWorkflowProjectDeployments");
        migrationBuilder.DropColumn("TaskConfigId", "AbpProWorkflowProjectRuns");
        migrationBuilder.DropColumn("PlcHandshakeConfigId", "AbpProWorkflowProjectRuns");
        migrationBuilder.DropColumn("PlcRequestId", "AbpProWorkflowProjectRuns");
        migrationBuilder.DropColumn("PlcRequestSequence", "AbpProWorkflowProjectRuns");
        migrationBuilder.DropColumn("RequestSequence", "AbpProWorkflowPlcHandshakeConfigs");
    }
}
