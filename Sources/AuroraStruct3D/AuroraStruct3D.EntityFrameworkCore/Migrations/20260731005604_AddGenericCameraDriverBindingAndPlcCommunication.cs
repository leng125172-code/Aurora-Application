using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddGenericCameraDriverBindingAndPlcCommunication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Some deployed databases predate these camera indexes even though the
            // migration history reports the camera migration as applied. Keep this
            // schema transition idempotent so those databases can still upgrade.
            migrationBuilder.Sql(
                """DROP INDEX IF EXISTS "IX_AbpProCameraDevices_DeviceIndex";""");
            migrationBuilder.Sql(
                """DROP INDEX IF EXISTS "IX_AbpProCameraDevices_DeviceSerialNumber";""");

            migrationBuilder.Sql(
                """
                ALTER TABLE "AbpProCameraDevices"
                    ADD COLUMN IF NOT EXISTS "Capabilities" integer NOT NULL DEFAULT 0;
                ALTER TABLE "AbpProCameraDevices"
                    ADD COLUMN IF NOT EXISTS "ConnectionSummary" character varying(256) NULL;
                ALTER TABLE "AbpProCameraDevices"
                    ADD COLUMN IF NOT EXISTS "DriverId" character varying(64) NOT NULL DEFAULT 'tucam';
                ALTER TABLE "AbpProCameraDevices"
                    ADD COLUMN IF NOT EXISTS "HardwareId" character varying(64) NULL;
                """);

            migrationBuilder.CreateTable(
                name: "AbpProPlcDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Protocol = table.Column<int>(type: "integer", nullable: false),
                    DriverId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    EndpointUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    AuthenticationType = table.Column<int>(type: "integer", nullable: false),
                    UserName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    EncryptedPassword = table.Column<string>(type: "text", nullable: true),
                    ClientCertificatePath = table.Column<string>(type: "text", nullable: true),
                    EncryptedClientCertificatePassword = table.Column<string>(type: "text", nullable: true),
                    SecurityPolicy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    MessageSecurityMode = table.Column<int>(type: "integer", nullable: false),
                    AutoTrustServerCertificate = table.Column<bool>(type: "boolean", nullable: false),
                    ConnectTimeoutMs = table.Column<int>(type: "integer", nullable: false),
                    OperationTimeoutMs = table.Column<int>(type: "integer", nullable: false),
                    SessionTimeoutMs = table.Column<int>(type: "integer", nullable: false),
                    KeepAliveMs = table.Column<int>(type: "integer", nullable: false),
                    ReconnectInitialMs = table.Column<int>(type: "integer", nullable: false),
                    ReconnectMaxMs = table.Column<int>(type: "integer", nullable: false),
                    IdleTimeoutMs = table.Column<int>(type: "integer", nullable: false),
                    ExtensionJson = table.Column<string>(type: "text", nullable: true),
                    ConnectionStatus = table.Column<int>(type: "integer", nullable: false),
                    LastConnectedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastFailedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
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
                    table.PrimaryKey("PK_AbpProPlcDevices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AbpProPlcOperationLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlcDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlcTagId = table.Column<Guid>(type: "uuid", nullable: true),
                    OperationType = table.Column<int>(type: "integer", nullable: false),
                    IsSuccess = table.Column<bool>(type: "boolean", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProPlcOperationLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbpProPlcOperationLogs_AbpProPlcDevices_PlcDeviceId",
                        column: x => x.PlcDeviceId,
                        principalTable: "AbpProPlcDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AbpProPlcTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlcDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Address = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    DataType = table.Column<int>(type: "integer", nullable: false),
                    Access = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SamplingIntervalMs = table.Column<int>(type: "integer", nullable: false),
                    Deadband = table.Column<double>(type: "double precision", nullable: true),
                    Scale = table.Column<double>(type: "double precision", nullable: false),
                    Offset = table.Column<double>(type: "double precision", nullable: false),
                    Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    DisplayFormat = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Minimum = table.Column<double>(type: "double precision", nullable: true),
                    Maximum = table.Column<double>(type: "double precision", nullable: true),
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
                    table.PrimaryKey("PK_AbpProPlcTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbpProPlcTags_AbpProPlcDevices_PlcDeviceId",
                        column: x => x.PlcDeviceId,
                        principalTable: "AbpProPlcDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AbpProPlcTrustedCertificates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlcDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Thumbprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Subject = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    NotBefore = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    NotAfter = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProPlcTrustedCertificates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbpProPlcTrustedCertificates_AbpProPlcDevices_PlcDeviceId",
                        column: x => x.PlcDeviceId,
                        principalTable: "AbpProPlcDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS "IX_AbpProCameraDevices_DeviceSerialNumber"
                    ON "AbpProCameraDevices" ("DeviceSerialNumber");
                CREATE INDEX IF NOT EXISTS "IX_AbpProCameraDevices_DriverId_DeviceIndex"
                    ON "AbpProCameraDevices" ("DriverId", "DeviceIndex");
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_AbpProCameraDevices_DriverId_HardwareId"
                    ON "AbpProCameraDevices" ("DriverId", "HardwareId");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AbpProPlcDevices_DriverId_EndpointUrl",
                table: "AbpProPlcDevices",
                columns: new[] { "DriverId", "EndpointUrl" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProPlcDevices_Name",
                table: "AbpProPlcDevices",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbpProPlcOperationLogs_PlcDeviceId_OccurredAt",
                table: "AbpProPlcOperationLogs",
                columns: new[] { "PlcDeviceId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProPlcTags_Code",
                table: "AbpProPlcTags",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbpProPlcTags_PlcDeviceId_Address",
                table: "AbpProPlcTags",
                columns: new[] { "PlcDeviceId", "Address" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbpProPlcTrustedCertificates_PlcDeviceId_Thumbprint",
                table: "AbpProPlcTrustedCertificates",
                columns: new[] { "PlcDeviceId", "Thumbprint" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AbpProPlcOperationLogs");

            migrationBuilder.DropTable(
                name: "AbpProPlcTags");

            migrationBuilder.DropTable(
                name: "AbpProPlcTrustedCertificates");

            migrationBuilder.DropTable(
                name: "AbpProPlcDevices");

            migrationBuilder.DropIndex(
                name: "IX_AbpProCameraDevices_DeviceSerialNumber",
                table: "AbpProCameraDevices");

            migrationBuilder.DropIndex(
                name: "IX_AbpProCameraDevices_DriverId_DeviceIndex",
                table: "AbpProCameraDevices");

            migrationBuilder.DropIndex(
                name: "IX_AbpProCameraDevices_DriverId_HardwareId",
                table: "AbpProCameraDevices");

            migrationBuilder.DropColumn(
                name: "Capabilities",
                table: "AbpProCameraDevices");

            migrationBuilder.DropColumn(
                name: "ConnectionSummary",
                table: "AbpProCameraDevices");

            migrationBuilder.DropColumn(
                name: "DriverId",
                table: "AbpProCameraDevices");

            migrationBuilder.DropColumn(
                name: "HardwareId",
                table: "AbpProCameraDevices");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCameraDevices_DeviceIndex",
                table: "AbpProCameraDevices",
                column: "DeviceIndex",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCameraDevices_DeviceSerialNumber",
                table: "AbpProCameraDevices",
                column: "DeviceSerialNumber",
                unique: true);
        }
    }
}
