using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <summary>
    /// 数据迁移：将 AbpProCalibMotorParams 中 OriginDirection 的值翻转。
    /// 原枚举：Positive=0 / Negative=1
    /// 新枚举：Negative=0 / Positive=1（与 LeisaiHomingDirection 统一）
    /// 翻转规则：旧值 0 → 新值 1，旧值 1 → 新值 0。
    /// </summary>
    public partial class FlipOriginDirectionValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 将旧 Positive(0) 暂存为 2，再把旧 Negative(1) 改为新 Negative(0)，最后将暂存值改为新 Positive(1)
            migrationBuilder.Sql(
                """
                UPDATE "AbpProCalibMotorParams"
                SET "OriginDirection" = CASE
                    WHEN "OriginDirection" = 0 THEN 1
                    WHEN "OriginDirection" = 1 THEN 0
                    ELSE "OriginDirection"
                END;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 回滚：翻转回原来的值（操作本身是幂等的）
            migrationBuilder.Sql(
                """
                UPDATE "AbpProCalibMotorParams"
                SET "OriginDirection" = CASE
                    WHEN "OriginDirection" = 0 THEN 1
                    WHEN "OriginDirection" = 1 THEN 0
                    ELSE "OriginDirection"
                END;
                """
            );
        }
    }
}
