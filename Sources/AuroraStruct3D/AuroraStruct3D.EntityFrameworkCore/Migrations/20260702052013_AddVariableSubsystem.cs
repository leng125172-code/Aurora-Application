using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddVariableSubsystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AbpProVariableDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerWorkflowId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TypeName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Visibility = table.Column<int>(type: "integer", nullable: false),
                    Mutability = table.Column<int>(type: "integer", nullable: false),
                    IsRequiredInit = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultValueJson = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    SnapshotVersion = table.Column<long>(type: "bigint", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProVariableDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AbpProVariableInstanceValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariableDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerWorkflowId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariableName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TypeName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    ValueJson = table.Column<string>(type: "text", nullable: true),
                    ValueVersion = table.Column<long>(type: "bigint", nullable: false),
                    LastWriterNodeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProVariableInstanceValues", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProVariableDefinitions_ProjectId_OwnerWorkflowId_Name",
                table: "AbpProVariableDefinitions",
                columns: new[] { "ProjectId", "OwnerWorkflowId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbpProVariableDefinitions_ProjectId_OwnerWorkflowId_Visibil~",
                table: "AbpProVariableDefinitions",
                columns: new[] { "ProjectId", "OwnerWorkflowId", "Visibility" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProVariableInstanceValues_ProjectId_InstanceId_OwnerWork~",
                table: "AbpProVariableInstanceValues",
                columns: new[] { "ProjectId", "InstanceId", "OwnerWorkflowId", "VariableName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbpProVariableInstanceValues_ProjectId_InstanceId_State",
                table: "AbpProVariableInstanceValues",
                columns: new[] { "ProjectId", "InstanceId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProVariableInstanceValues_ProjectId_InstanceId_VariableD~",
                table: "AbpProVariableInstanceValues",
                columns: new[] { "ProjectId", "InstanceId", "VariableDefinitionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AbpProVariableDefinitions");

            migrationBuilder.DropTable(
                name: "AbpProVariableInstanceValues");
        }
    }
}
