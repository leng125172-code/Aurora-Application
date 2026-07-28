using AuroraStruct3D.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations;

[DbContext(typeof(AuroraStruct3DDbContext))]
[Migration("20260727121000_AddWorkflowSourceHistory")]
public partial class AddWorkflowSourceHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AbpProWorkflowSourceDrafts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ExtraProperties = table.Column<string>(type: "text", nullable: false),
                ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                WorkflowId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: true),
                BaseRevision = table.Column<int>(type: "integer", nullable: false),
                SourceCode = table.Column<string>(type: "text", nullable: false),
                GraphData = table.Column<string>(type: "text", nullable: false),
                ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
            },
            constraints: table => table.PrimaryKey("PK_AbpProWorkflowSourceDrafts", x => x.Id)
        );
        migrationBuilder.CreateTable(
            name: "AbpProWorkflowSourceVersions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ExtraProperties = table.Column<string>(type: "text", nullable: false),
                ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                WorkflowId = table.Column<Guid>(type: "uuid", nullable: false),
                Revision = table.Column<int>(type: "integer", nullable: false),
                SourceCode = table.Column<string>(type: "text", nullable: false),
                GraphData = table.Column<string>(type: "text", nullable: false),
                ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                SemanticHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ProgramHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
            },
            constraints: table => table.PrimaryKey("PK_AbpProWorkflowSourceVersions", x => x.Id)
        );
        migrationBuilder.CreateIndex(
            name: "IX_AbpProWorkflowSourceDrafts_WorkflowId_UserId",
            table: "AbpProWorkflowSourceDrafts",
            columns: new[] { "WorkflowId", "UserId" },
            unique: true
        );
        migrationBuilder.CreateIndex(
            name: "IX_AbpProWorkflowSourceVersions_WorkflowId_Revision",
            table: "AbpProWorkflowSourceVersions",
            columns: new[] { "WorkflowId", "Revision" },
            unique: true
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("AbpProWorkflowSourceDrafts");
        migrationBuilder.DropTable("AbpProWorkflowSourceVersions");
    }
}
