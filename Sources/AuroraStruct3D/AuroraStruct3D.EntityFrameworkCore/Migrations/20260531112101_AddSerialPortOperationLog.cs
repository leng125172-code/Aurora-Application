using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddSerialPortOperationLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AbpProSerialPortOperationLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SerialPortConfigId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsSuccess = table.Column<bool>(type: "boolean", nullable: false),
                    ParameterSummary = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    RoundTripMs = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProSerialPortOperationLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbpProSerialPortOperationLogs_AbpProSerialPortConfigs_Seria~",
                        column: x => x.SerialPortConfigId,
                        principalTable: "AbpProSerialPortConfigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProSerialPortOperationLogs_OccurredAt",
                table: "AbpProSerialPortOperationLogs",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProSerialPortOperationLogs_SerialPortConfigId",
                table: "AbpProSerialPortOperationLogs",
                column: "SerialPortConfigId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProSerialPortOperationLogs_SerialPortConfigId_IsSuccess",
                table: "AbpProSerialPortOperationLogs",
                columns: new[] { "SerialPortConfigId", "IsSuccess" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProSerialPortOperationLogs_SerialPortConfigId_OccurredAt",
                table: "AbpProSerialPortOperationLogs",
                columns: new[] { "SerialPortConfigId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProSerialPortOperationLogs_SerialPortConfigId_OperationT~",
                table: "AbpProSerialPortOperationLogs",
                columns: new[] { "SerialPortConfigId", "OperationType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AbpProSerialPortOperationLogs");
        }
    }
}
