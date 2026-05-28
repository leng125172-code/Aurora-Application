using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddProductModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AbpProProductModels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    FileFormat = table.Column<int>(type: "integer", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    OriginalBlobName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConvertedBlobName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ConversionStatus = table.Column<int>(type: "integer", nullable: false),
                    ConversionErrorMessage = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
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
                    table.PrimaryKey("PK_AbpProProductModels", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProProductModels_ConversionStatus",
                table: "AbpProProductModels",
                column: "ConversionStatus");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProProductModels_CreationTime",
                table: "AbpProProductModels",
                column: "CreationTime");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProProductModels_CreatorId",
                table: "AbpProProductModels",
                column: "CreatorId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProProductModels_FileFormat",
                table: "AbpProProductModels",
                column: "FileFormat");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AbpProProductModels");
        }
    }
}
