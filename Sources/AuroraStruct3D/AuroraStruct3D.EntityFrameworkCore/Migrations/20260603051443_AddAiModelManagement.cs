using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddAiModelManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AbpProAiModelOperationLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AiModelId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModelName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    OperationType = table.Column<int>(type: "integer", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsSuccess = table.Column<bool>(type: "boolean", nullable: false),
                    ParameterSummary = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProAiModelOperationLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AbpProAiModels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    OriginalFileName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ModelIdentifier = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FileFormat = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    BlobName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Platform = table.Column<int>(type: "integer", nullable: false),
                    LocationKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    GenerationCondition = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    LoadStatus = table.Column<int>(type: "integer", nullable: false),
                    LoadErrorMessage = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    LastLoadedTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
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
                    table.PrimaryKey("PK_AbpProAiModels", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProAiModelOperationLogs_AiModelId",
                table: "AbpProAiModelOperationLogs",
                column: "AiModelId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProAiModelOperationLogs_IsSuccess",
                table: "AbpProAiModelOperationLogs",
                column: "IsSuccess");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProAiModelOperationLogs_OccurredAt",
                table: "AbpProAiModelOperationLogs",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProAiModelOperationLogs_OperationType",
                table: "AbpProAiModelOperationLogs",
                column: "OperationType");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProAiModels_CreationTime",
                table: "AbpProAiModels",
                column: "CreationTime");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProAiModels_CreatorId",
                table: "AbpProAiModels",
                column: "CreatorId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProAiModels_LoadStatus",
                table: "AbpProAiModels",
                column: "LoadStatus");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProAiModels_ModelIdentifier",
                table: "AbpProAiModels",
                column: "ModelIdentifier");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProAiModels_Platform",
                table: "AbpProAiModels",
                column: "Platform");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AbpProAiModelOperationLogs");

            migrationBuilder.DropTable(
                name: "AbpProAiModels");
        }
    }
}
