using AuroraStruct3D.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.Migrations;

[DbContext(typeof(AuroraStruct3DDbContext))]
[Migration("20260803084500_AddWorkflowProjectTaskName")]
public partial class AddWorkflowProjectTaskName : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Name",
            table: "AbpProWorkflowProjectTaskConfigs",
            type: "character varying(128)",
            maxLength: 128,
            nullable: false,
            defaultValue: "默认任务");

        migrationBuilder.Sql("""
            UPDATE "AbpProWorkflowProjectTaskConfigs"
            SET "Name" = '任务-' || LEFT("Id"::text, 8)
            WHERE "Name" = '默认任务';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Name",
            table: "AbpProWorkflowProjectTaskConfigs");
    }
}
