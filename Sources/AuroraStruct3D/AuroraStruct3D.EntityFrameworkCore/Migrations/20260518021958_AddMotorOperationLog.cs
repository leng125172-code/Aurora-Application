using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddMotorOperationLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AbpProMotorOperationLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MotorAxisId = table.Column<Guid>(type: "uuid", nullable: false),
                    SlaveId = table.Column<int>(type: "integer", nullable: false),
                    OperationType = table.Column<int>(type: "integer", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsSuccess = table.Column<bool>(type: "boolean", nullable: false),
                    CommandCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ParameterSummary = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    RoundTripMs = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProMotorOperationLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbpProMotorOperationLogs_AbpProMotorAxes_MotorAxisId",
                        column: x => x.MotorAxisId,
                        principalTable: "AbpProMotorAxes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProMotorOperationLogs_MotorAxisId",
                table: "AbpProMotorOperationLogs",
                column: "MotorAxisId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProMotorOperationLogs_MotorAxisId_IsSuccess",
                table: "AbpProMotorOperationLogs",
                columns: new[] { "MotorAxisId", "IsSuccess" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProMotorOperationLogs_MotorAxisId_OccurredAt",
                table: "AbpProMotorOperationLogs",
                columns: new[] { "MotorAxisId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProMotorOperationLogs_MotorAxisId_OperationType",
                table: "AbpProMotorOperationLogs",
                columns: new[] { "MotorAxisId", "OperationType" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProMotorOperationLogs_OccurredAt",
                table: "AbpProMotorOperationLogs",
                column: "OccurredAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AbpProMotorOperationLogs");
        }
    }
}
