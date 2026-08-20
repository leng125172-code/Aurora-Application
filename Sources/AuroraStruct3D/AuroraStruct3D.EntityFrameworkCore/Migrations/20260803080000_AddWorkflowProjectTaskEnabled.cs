using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations;

[DbContext(typeof(AuroraStruct3DDbContext))]
[Migration("20260803080000_AddWorkflowProjectTaskEnabled")]
public partial class AddWorkflowProjectTaskEnabled : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsEnabled",
            table: "AbpProWorkflowProjectTaskConfigs",
            type: "boolean",
            nullable: false,
            defaultValue: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "IsEnabled",
            table: "AbpProWorkflowProjectTaskConfigs");
    }
}
