using AuroraStruct3D.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations;

[DbContext(typeof(AuroraStruct3DDbContext))]
[Migration("20260727090000_AddWorkflowScriptSource")]
public partial class AddWorkflowScriptSource : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SourceCode",
            table: "AbpProWorkflowDefinitions",
            type: "text",
            nullable: true
        );
        migrationBuilder.AddColumn<string>(
            name: "SourceHash",
            table: "AbpProWorkflowDefinitions",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true
        );
        migrationBuilder.AddColumn<string>(
            name: "ProgramHash",
            table: "AbpProWorkflowDefinitions",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true
        );
        migrationBuilder.AddColumn<int>(
            name: "LanguageVersion",
            table: "AbpProWorkflowDefinitions",
            type: "integer",
            nullable: false,
            defaultValue: 1
        );
        migrationBuilder.AddColumn<int>(
            name: "SourceRevision",
            table: "AbpProWorkflowDefinitions",
            type: "integer",
            nullable: false,
            defaultValue: 0
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "SourceCode", table: "AbpProWorkflowDefinitions");
        migrationBuilder.DropColumn(name: "SourceHash", table: "AbpProWorkflowDefinitions");
        migrationBuilder.DropColumn(name: "ProgramHash", table: "AbpProWorkflowDefinitions");
        migrationBuilder.DropColumn(name: "LanguageVersion", table: "AbpProWorkflowDefinitions");
        migrationBuilder.DropColumn(name: "SourceRevision", table: "AbpProWorkflowDefinitions");
    }
}
