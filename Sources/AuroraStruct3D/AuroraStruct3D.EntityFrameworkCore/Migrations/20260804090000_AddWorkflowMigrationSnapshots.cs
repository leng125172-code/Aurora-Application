using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations;

[DbContext(typeof(AuroraStruct3DDbContext))]
[Migration("20260804090000_AddWorkflowMigrationSnapshots")]
public partial class AddWorkflowMigrationSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AbpProWorkflowMigrationSnapshots",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                WorkflowId = table.Column<Guid>(type: "uuid", nullable: false),
                WorkflowName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                SourceCode = table.Column<string>(type: "text", nullable: true),
                GraphData = table.Column<string>(type: "text", nullable: false),
                LanguageVersion = table.Column<int>(type: "integer", nullable: false),
                SourceRevision = table.Column<int>(type: "integer", nullable: false),
                SourceHash = table.Column<string>(type: "text", nullable: true),
                SemanticHash = table.Column<string>(type: "text", nullable: true),
                ProgramHash = table.Column<string>(type: "text", nullable: true),
                OperatorContractHash = table.Column<string>(type: "text", nullable: true),
                RolledBackAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                ExtraProperties = table.Column<string>(type: "text", nullable: false),
                ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_AbpProWorkflowMigrationSnapshots", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_AbpProWorkflowMigrationSnapshots_BatchId_WorkflowId",
            table: "AbpProWorkflowMigrationSnapshots",
            columns: new[] { "BatchId", "WorkflowId" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_AbpProWorkflowMigrationSnapshots_WorkflowId",
            table: "AbpProWorkflowMigrationSnapshots",
            column: "WorkflowId");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "AbpProWorkflowMigrationSnapshots");
}
