using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddSerialPortConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AbpProMotorAxes_PortName_SlaveId",
                table: "AbpProMotorAxes");

            migrationBuilder.DropColumn(
                name: "BaudRate",
                table: "AbpProMotorAxes");

            migrationBuilder.DropColumn(
                name: "PortName",
                table: "AbpProMotorAxes");

            migrationBuilder.AddColumn<Guid>(
                name: "SerialPortConfigId",
                table: "AbpProMotorAxes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "AbpProSerialPortConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    PortName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BaudRate = table.Column<int>(type: "integer", nullable: false),
                    DataBits = table.Column<int>(type: "integer", nullable: false),
                    Parity = table.Column<int>(type: "integer", nullable: false),
                    StopBits = table.Column<int>(type: "integer", nullable: false),
                    Handshake = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_AbpProSerialPortConfigs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProMotorAxes_SerialPortConfigId_SlaveId",
                table: "AbpProMotorAxes",
                columns: new[] { "SerialPortConfigId", "SlaveId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbpProSerialPortConfigs_IsEnabled",
                table: "AbpProSerialPortConfigs",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProSerialPortConfigs_PortName",
                table: "AbpProSerialPortConfigs",
                column: "PortName",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AbpProMotorAxes_AbpProSerialPortConfigs_SerialPortConfigId",
                table: "AbpProMotorAxes",
                column: "SerialPortConfigId",
                principalTable: "AbpProSerialPortConfigs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AbpProMotorAxes_AbpProSerialPortConfigs_SerialPortConfigId",
                table: "AbpProMotorAxes");

            migrationBuilder.DropTable(
                name: "AbpProSerialPortConfigs");

            migrationBuilder.DropIndex(
                name: "IX_AbpProMotorAxes_SerialPortConfigId_SlaveId",
                table: "AbpProMotorAxes");

            migrationBuilder.DropColumn(
                name: "SerialPortConfigId",
                table: "AbpProMotorAxes");

            migrationBuilder.AddColumn<int>(
                name: "BaudRate",
                table: "AbpProMotorAxes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PortName",
                table: "AbpProMotorAxes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProMotorAxes_PortName_SlaveId",
                table: "AbpProMotorAxes",
                columns: new[] { "PortName", "SlaveId" },
                unique: true);
        }
    }
}
