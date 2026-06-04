using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class RefactorAiModelToMasterDetail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConversionPreference",
                table: "AbpProAiModels",
                type: "integer",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<int>(
                name: "FileCount",
                table: "AbpProAiModels",
                type: "integer",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<int>(
                name: "ResolvedConversionType",
                table: "AbpProAiModels",
                type: "integer",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.CreateTable(
                name: "AbpProAiModelFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AiModelId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalFileName = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: false
                    ),
                    DisplayName = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: false
                    ),
                    FileFormat = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Md5 = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    BlobName = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    FileRole = table.Column<int>(type: "integer", nullable: false),
                    Md5Verified = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(
                        type: "character varying(40)",
                        maxLength: 40,
                        nullable: false
                    ),
                    CreationTime = table.Column<DateTime>(
                        type: "timestamp without time zone",
                        nullable: false
                    ),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(
                        type: "timestamp without time zone",
                        nullable: true
                    ),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(
                        type: "boolean",
                        nullable: false,
                        defaultValue: false
                    ),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(
                        type: "timestamp without time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProAiModelFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbpProAiModelFiles_AbpProAiModels_AiModelId",
                        column: x => x.AiModelId,
                        principalTable: "AbpProAiModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.Sql(
                """
INSERT INTO "AbpProAiModelFiles" (
    "Id",
    "AiModelId",
    "OriginalFileName",
    "DisplayName",
    "FileFormat",
    "FileSizeBytes",
    "Md5",
    "BlobName",
    "FileRole",
    "Md5Verified",
    "SortOrder",
    "ExtraProperties",
    "ConcurrencyStamp",
    "CreationTime",
    "CreatorId",
    "LastModificationTime",
    "LastModifierId",
    "IsDeleted",
    "DeleterId",
    "DeletionTime"
)
SELECT
    "Id",
    "Id",
    "OriginalFileName",
    CASE
        WHEN POSITION('.' IN REVERSE("OriginalFileName")) > 0 THEN LEFT("OriginalFileName", LENGTH("OriginalFileName") - POSITION('.' IN REVERSE("OriginalFileName")))
        ELSE "OriginalFileName"
    END,
    "FileFormat",
    "FileSizeBytes",
    "Md5",
    "BlobName",
    0,
    TRUE,
    0,
    ''::text,
    SUBSTRING(MD5("Id"::text || '-ai-model-file') FROM 1 FOR 40),
    "CreationTime",
    "CreatorId",
    "LastModificationTime",
    "LastModifierId",
    "IsDeleted",
    "DeleterId",
    "DeletionTime"
FROM "AbpProAiModels"
WHERE COALESCE("OriginalFileName", '') <> '';
"""
            );

            migrationBuilder.Sql(
                """
UPDATE "AbpProAiModels"
SET "FileCount" = CASE
    WHEN COALESCE("OriginalFileName", '') <> '' THEN 1
    ELSE 0
END;
"""
            );

            migrationBuilder.DropIndex(name: "IX_AbpProAiModels_Md5", table: "AbpProAiModels");

            migrationBuilder.DropColumn(name: "BlobName", table: "AbpProAiModels");

            migrationBuilder.DropColumn(name: "FileFormat", table: "AbpProAiModels");

            migrationBuilder.DropColumn(name: "FileSizeBytes", table: "AbpProAiModels");

            migrationBuilder.DropColumn(name: "Md5", table: "AbpProAiModels");

            migrationBuilder.DropColumn(name: "OriginalFileName", table: "AbpProAiModels");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProAiModels_ConversionPreference",
                table: "AbpProAiModels",
                column: "ConversionPreference"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProAiModels_LocationKey",
                table: "AbpProAiModels",
                column: "LocationKey"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProAiModels_ResolvedConversionType",
                table: "AbpProAiModels",
                column: "ResolvedConversionType"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProAiModelFiles_AiModelId",
                table: "AbpProAiModelFiles",
                column: "AiModelId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProAiModelFiles_AiModelId_SortOrder",
                table: "AbpProAiModelFiles",
                columns: new[] { "AiModelId", "SortOrder" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProAiModelFiles_FileFormat",
                table: "AbpProAiModelFiles",
                column: "FileFormat"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProAiModelFiles_FileRole",
                table: "AbpProAiModelFiles",
                column: "FileRole"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProAiModelFiles_Md5",
                table: "AbpProAiModelFiles",
                column: "Md5"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AbpProAiModels_ConversionPreference",
                table: "AbpProAiModels"
            );

            migrationBuilder.DropIndex(
                name: "IX_AbpProAiModels_LocationKey",
                table: "AbpProAiModels"
            );

            migrationBuilder.DropIndex(
                name: "IX_AbpProAiModels_ResolvedConversionType",
                table: "AbpProAiModels"
            );

            migrationBuilder.DropColumn(name: "ConversionPreference", table: "AbpProAiModels");

            migrationBuilder.DropColumn(name: "FileCount", table: "AbpProAiModels");

            migrationBuilder.DropColumn(name: "ResolvedConversionType", table: "AbpProAiModels");

            migrationBuilder.AddColumn<string>(
                name: "BlobName",
                table: "AbpProAiModels",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<string>(
                name: "FileFormat",
                table: "AbpProAiModels",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<long>(
                name: "FileSizeBytes",
                table: "AbpProAiModels",
                type: "bigint",
                nullable: false,
                defaultValue: 0L
            );

            migrationBuilder.AddColumn<string>(
                name: "Md5",
                table: "AbpProAiModels",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<string>(
                name: "OriginalFileName",
                table: "AbpProAiModels",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.Sql(
                """
UPDATE "AbpProAiModels" AS m
SET
    "BlobName" = COALESCE(src."BlobName", ''),
    "FileFormat" = COALESCE(src."FileFormat", ''),
    "FileSizeBytes" = COALESCE(src."FileSizeBytes", 0),
    "Md5" = COALESCE(src."Md5", ''),
    "OriginalFileName" = COALESCE(src."OriginalFileName", '')
FROM (
    SELECT DISTINCT ON ("AiModelId")
        "AiModelId",
        "BlobName",
        "FileFormat",
        "FileSizeBytes",
        "Md5",
        "OriginalFileName"
    FROM "AbpProAiModelFiles"
    ORDER BY "AiModelId", "SortOrder", "CreationTime"
) AS src
WHERE m."Id" = src."AiModelId";
"""
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProAiModels_Md5",
                table: "AbpProAiModels",
                column: "Md5"
            );

            migrationBuilder.DropTable(name: "AbpProAiModelFiles");
        }
    }
}
