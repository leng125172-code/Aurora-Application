using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations;

[DbContext(typeof(AuroraStruct3DDbContext))]
[Migration("20260803065000_FreezeWorkflowTaskSnapshot")]
public partial class FreezeWorkflowTaskSnapshot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "OnErrorAction", table: "AbpProWorkflowProjectTaskConfigs", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<string>(name: "FrozenTaskConfigJson", table: "AbpProWorkflowProjectDeployments", type: "character varying(4096)", maxLength: 4096, nullable: false, defaultValue: "{}");
        migrationBuilder.AddColumn<int>(name: "SnapshotSchemaVersion", table: "AbpProWorkflowProjectDeployments", type: "integer", nullable: false, defaultValue: 2);

        migrationBuilder.Sql("""
            UPDATE "AbpProWorkflowProjectDeployments" AS d
            SET "FrozenTaskConfigJson" = json_build_object(
                'TaskType', t."TaskType", 'CycleIntervalSeconds', t."CycleIntervalSeconds",
                'ResultWorkflowId', t."ResultWorkflowId", 'ResultVariableName', t."ResultVariableName",
                'OnErrorAction', t."OnErrorAction")::text
            FROM "AbpProWorkflowProjectTaskConfigs" AS t
            WHERE t."ProjectId" = d."ProjectId";
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "OnErrorAction", table: "AbpProWorkflowProjectTaskConfigs");
        migrationBuilder.DropColumn(name: "FrozenTaskConfigJson", table: "AbpProWorkflowProjectDeployments");
        migrationBuilder.DropColumn(name: "SnapshotSchemaVersion", table: "AbpProWorkflowProjectDeployments");
    }
}
