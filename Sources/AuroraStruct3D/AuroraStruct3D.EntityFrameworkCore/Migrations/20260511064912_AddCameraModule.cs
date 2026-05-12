using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddCameraModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AbpProCameraDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Model = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    SerialNumber = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: true
                    ),
                    DeviceIndex = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: true
                    ),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ActiveParameterSetId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_AbpProCameraDevices", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "AbpProCameraParameterSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CameraDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Description = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: true
                    ),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_AbpProCameraParameterSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbpProCameraParameterSets_AbpProCameraDevices_CameraDeviceId",
                        column: x => x.CameraDeviceId,
                        principalTable: "AbpProCameraDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "AbpProCameraParameters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParameterSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParamKey = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    ParamType = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: false
                    ),
                    Description = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProCameraParameters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbpProCameraParameters_AbpProCameraParameterSets_ParameterS~",
                        column: x => x.ParameterSetId,
                        principalTable: "AbpProCameraParameterSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCameraDevices_DeviceIndex",
                table: "AbpProCameraDevices",
                column: "DeviceIndex",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCameraDevices_IsEnabled",
                table: "AbpProCameraDevices",
                column: "IsEnabled"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCameraParameters_ParameterSetId",
                table: "AbpProCameraParameters",
                column: "ParameterSetId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCameraParameters_ParameterSetId_ParamKey",
                table: "AbpProCameraParameters",
                columns: new[] { "ParameterSetId", "ParamKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCameraParameterSets_CameraDeviceId",
                table: "AbpProCameraParameterSets",
                column: "CameraDeviceId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCameraParameterSets_CameraDeviceId_IsDefault",
                table: "AbpProCameraParameterSets",
                columns: new[] { "CameraDeviceId", "IsDefault" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AbpProCameraParameters");

            migrationBuilder.DropTable(name: "AbpProCameraParameterSets");

            migrationBuilder.DropTable(name: "AbpProCameraDevices");
        }
    }
}
