using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddProductModelOperationLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AbpProProductModelOperationLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductModelId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_AbpProProductModelOperationLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProProductModelOperationLogs_IsSuccess",
                table: "AbpProProductModelOperationLogs",
                column: "IsSuccess");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProProductModelOperationLogs_OccurredAt",
                table: "AbpProProductModelOperationLogs",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProProductModelOperationLogs_OperationType",
                table: "AbpProProductModelOperationLogs",
                column: "OperationType");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProProductModelOperationLogs_ProductModelId",
                table: "AbpProProductModelOperationLogs",
                column: "ProductModelId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AbpProProductModelOperationLogs");
        }
    }
}
