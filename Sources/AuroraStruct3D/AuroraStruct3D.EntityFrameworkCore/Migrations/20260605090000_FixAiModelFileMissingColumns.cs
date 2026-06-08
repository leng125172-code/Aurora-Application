using AuroraStruct3D.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    [DbContext(typeof(AuroraStruct3DDbContext))]
    [Migration("20260605090000_FixAiModelFileMissingColumns")]
    public class FixAiModelFileMissingColumns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"AbpProAiModelFiles\" ADD COLUMN IF NOT EXISTS \"IsOriginalFile\" boolean NOT NULL DEFAULT TRUE;"
            );

            migrationBuilder.Sql(
                "ALTER TABLE \"AbpProAiModelFiles\" ADD COLUMN IF NOT EXISTS \"IsConvertedFile\" boolean NOT NULL DEFAULT FALSE;"
            );

            migrationBuilder.Sql(
                "ALTER TABLE \"AbpProAiModelFiles\" ADD COLUMN IF NOT EXISTS \"SourceFileId\" uuid NULL;"
            );

            migrationBuilder.Sql(
                "ALTER TABLE \"AbpProAiModelFiles\" ADD COLUMN IF NOT EXISTS \"ConversionTargetType\" integer NULL;"
            );

            migrationBuilder.Sql(
                "ALTER TABLE \"AbpProAiModelFiles\" ADD COLUMN IF NOT EXISTS \"ConversionStatus\" integer NOT NULL DEFAULT 0;"
            );

            migrationBuilder.Sql(
                "ALTER TABLE \"AbpProAiModelFiles\" ADD COLUMN IF NOT EXISTS \"ConversionErrorMessage\" character varying(2048) NULL;"
            );

            migrationBuilder.Sql(
                "ALTER TABLE \"AbpProAiModelFiles\" ADD COLUMN IF NOT EXISTS \"ConversionTime\" timestamp without time zone NULL;"
            );

            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_AbpProAiModelFiles_IsOriginalFile\" ON \"AbpProAiModelFiles\" (\"IsOriginalFile\");"
            );

            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_AbpProAiModelFiles_IsConvertedFile\" ON \"AbpProAiModelFiles\" (\"IsConvertedFile\");"
            );

            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_AbpProAiModelFiles_SourceFileId\" ON \"AbpProAiModelFiles\" (\"SourceFileId\");"
            );

            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_AbpProAiModelFiles_ConversionTargetType\" ON \"AbpProAiModelFiles\" (\"ConversionTargetType\");"
            );

            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_AbpProAiModelFiles_ConversionStatus\" ON \"AbpProAiModelFiles\" (\"ConversionStatus\");"
            );
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_AbpProAiModelFiles_ConversionStatus\";"
            );

            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_AbpProAiModelFiles_ConversionTargetType\";"
            );

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_AbpProAiModelFiles_SourceFileId\";");

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_AbpProAiModelFiles_IsConvertedFile\";");

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_AbpProAiModelFiles_IsOriginalFile\";");

            migrationBuilder.Sql(
                "ALTER TABLE \"AbpProAiModelFiles\" DROP COLUMN IF EXISTS \"ConversionTime\";"
            );

            migrationBuilder.Sql(
                "ALTER TABLE \"AbpProAiModelFiles\" DROP COLUMN IF EXISTS \"ConversionErrorMessage\";"
            );

            migrationBuilder.Sql(
                "ALTER TABLE \"AbpProAiModelFiles\" DROP COLUMN IF EXISTS \"ConversionStatus\";"
            );

            migrationBuilder.Sql(
                "ALTER TABLE \"AbpProAiModelFiles\" DROP COLUMN IF EXISTS \"ConversionTargetType\";"
            );

            migrationBuilder.Sql(
                "ALTER TABLE \"AbpProAiModelFiles\" DROP COLUMN IF EXISTS \"SourceFileId\";"
            );

            migrationBuilder.Sql(
                "ALTER TABLE \"AbpProAiModelFiles\" DROP COLUMN IF EXISTS \"IsConvertedFile\";"
            );

            migrationBuilder.Sql(
                "ALTER TABLE \"AbpProAiModelFiles\" DROP COLUMN IF EXISTS \"IsOriginalFile\";"
            );
        }
    }
}
