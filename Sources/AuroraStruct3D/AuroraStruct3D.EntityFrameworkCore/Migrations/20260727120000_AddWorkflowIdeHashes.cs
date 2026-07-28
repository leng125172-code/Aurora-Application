using AuroraStruct3D.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations;

[DbContext(typeof(AuroraStruct3DDbContext))]
[Migration("20260727120000_AddWorkflowIdeHashes")]
public partial class AddWorkflowIdeHashes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SemanticHash",
            table: "AbpProWorkflowDefinitions",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true
        );
        migrationBuilder.AddColumn<string>(
            name: "OperatorContractHash",
            table: "AbpProWorkflowDefinitions",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("SemanticHash", "AbpProWorkflowDefinitions");
        migrationBuilder.DropColumn("OperatorContractHash", "AbpProWorkflowDefinitions");
    }
}
