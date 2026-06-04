using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class RefactorAiModelStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AbpProAiModels_LoadStatus",
                table: "AbpProAiModels");

            migrationBuilder.DropIndex(
                name: "IX_AbpProAiModels_ModelIdentifier",
                table: "AbpProAiModels");

            migrationBuilder.DropIndex(
                name: "IX_AbpProAiModels_Platform",
                table: "AbpProAiModels");

            migrationBuilder.DropColumn(
                name: "LastLoadedTime",
                table: "AbpProAiModels");

            migrationBuilder.DropColumn(
                name: "LoadErrorMessage",
                table: "AbpProAiModels");

            migrationBuilder.DropColumn(
                name: "LoadStatus",
                table: "AbpProAiModels");

            migrationBuilder.DropColumn(
                name: "ModelIdentifier",
                table: "AbpProAiModels");

            migrationBuilder.DropColumn(
                name: "Platform",
                table: "AbpProAiModels");

            migrationBuilder.AddColumn<string>(
                name: "Md5",
                table: "AbpProAiModels",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "AbpProAiModelIdentifiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
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
                    table.PrimaryKey("PK_AbpProAiModelIdentifiers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AbpProAiModelIdentifierLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AiModelId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentifierId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProAiModelIdentifierLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbpProAiModelIdentifierLinks_AbpProAiModelIdentifiers_Ident~",
                        column: x => x.IdentifierId,
                        principalTable: "AbpProAiModelIdentifiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AbpProAiModelIdentifierLinks_AbpProAiModels_AiModelId",
                        column: x => x.AiModelId,
                        principalTable: "AbpProAiModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProAiModels_Md5",
                table: "AbpProAiModels",
                column: "Md5");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProAiModelIdentifierLinks_AiModelId",
                table: "AbpProAiModelIdentifierLinks",
                column: "AiModelId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProAiModelIdentifierLinks_AiModelId_IdentifierId",
                table: "AbpProAiModelIdentifierLinks",
                columns: new[] { "AiModelId", "IdentifierId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbpProAiModelIdentifierLinks_IdentifierId",
                table: "AbpProAiModelIdentifierLinks",
                column: "IdentifierId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProAiModelIdentifiers_NormalizedName",
                table: "AbpProAiModelIdentifiers",
                column: "NormalizedName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AbpProAiModelIdentifierLinks");

            migrationBuilder.DropTable(
                name: "AbpProAiModelIdentifiers");

            migrationBuilder.DropIndex(
                name: "IX_AbpProAiModels_Md5",
                table: "AbpProAiModels");

            migrationBuilder.DropColumn(
                name: "Md5",
                table: "AbpProAiModels");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastLoadedTime",
                table: "AbpProAiModels",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LoadErrorMessage",
                table: "AbpProAiModels",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LoadStatus",
                table: "AbpProAiModels",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ModelIdentifier",
                table: "AbpProAiModels",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Platform",
                table: "AbpProAiModels",
                type: "integer",
                nullable: false,
                defaultValue: 0);

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
    }
}
