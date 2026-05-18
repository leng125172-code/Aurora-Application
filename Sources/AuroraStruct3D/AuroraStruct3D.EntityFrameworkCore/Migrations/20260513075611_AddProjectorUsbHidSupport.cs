using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectorUsbHidSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. 清理旧串口 Migration 残留（可能未执行过，全部使用 IF EXISTS） ─────────
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_AbpProProjectors_SerialPortPath";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "AbpProProjectors" DROP COLUMN IF EXISTS "SerialPortPath";
                """);

            // ── 2. 添加 ConnectionType 列（串口 Migration 未执行时不存在） ──────────────
            migrationBuilder.Sql("""
                ALTER TABLE "AbpProProjectors" ADD COLUMN IF NOT EXISTS "ConnectionType" integer NOT NULL DEFAULT 0;
                """);

            // ── 3. IpAddress 改为可空（原建表为 NOT NULL，HID 模式不需要 IP） ───────────
            migrationBuilder.Sql("""
                ALTER TABLE "AbpProProjectors" ALTER COLUMN "IpAddress" DROP NOT NULL;
                """);

            // ── 4. SerialBaudRate → HidVendorId（可能不存在，先检查再改名或直接添加） ──
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'AbpProProjectors' AND column_name = 'SerialBaudRate'
                    ) THEN
                        ALTER TABLE "AbpProProjectors" RENAME COLUMN "SerialBaudRate" TO "HidVendorId";
                    ELSIF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'AbpProProjectors' AND column_name = 'HidVendorId'
                    ) THEN
                        ALTER TABLE "AbpProProjectors" ADD COLUMN "HidVendorId" integer NOT NULL DEFAULT 0;
                    END IF;
                END $$;
                """);

            // ── 5. 添加其余 HID 列 ────────────────────────────────────────────────────
            migrationBuilder.Sql("""
                ALTER TABLE "AbpProProjectors" ADD COLUMN IF NOT EXISTS "HidProductId" integer NOT NULL DEFAULT 0;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "AbpProProjectors" ADD COLUMN IF NOT EXISTS "HidDeviceIndex" integer NOT NULL DEFAULT 0;
                """);

            // ── 6. 添加索引 ───────────────────────────────────────────────────────────
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_AbpProProjectors_ConnectionType"
                ON "AbpProProjectors" ("ConnectionType");
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_AbpProProjectors_HidVendorId_HidProductId_HidDeviceIndex"
                ON "AbpProProjectors" ("HidVendorId", "HidProductId", "HidDeviceIndex")
                WHERE "ConnectionType" = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AbpProProjectors_HidVendorId_HidProductId_HidDeviceIndex",
                table: "AbpProProjectors");

            migrationBuilder.DropColumn(
                name: "HidDeviceIndex",
                table: "AbpProProjectors");

            migrationBuilder.DropColumn(
                name: "HidProductId",
                table: "AbpProProjectors");

            migrationBuilder.RenameColumn(
                name: "HidVendorId",
                table: "AbpProProjectors",
                newName: "SerialBaudRate");

            migrationBuilder.AddColumn<string>(
                name: "SerialPortPath",
                table: "AbpProProjectors",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbpProProjectors_SerialPortPath",
                table: "AbpProProjectors",
                column: "SerialPortPath",
                unique: true,
                filter: "\"SerialPortPath\" IS NOT NULL");
        }
    }
}
