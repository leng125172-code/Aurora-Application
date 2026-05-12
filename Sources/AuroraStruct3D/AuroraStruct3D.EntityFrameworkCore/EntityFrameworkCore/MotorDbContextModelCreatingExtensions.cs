using AuroraStruct3D.Motors;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AuroraStruct3D.EntityFrameworkCore;

/// <summary>
/// 电机模块数据库配置扩展
/// </summary>
public static class MotorDbContextModelCreatingExtensions
{
    private const string TablePrefix = AuroraStruct3DDbProperties.DbTablePrefix;

    /// <summary>
    /// 配置电机相关数据库表结构
    /// </summary>
    public static void ConfigureMotor(this ModelBuilder builder)
    {
        // ── 电机轴表 ──────────────────────────────────────────────────────────────
        builder.Entity<MotorAxis>(b =>
        {
            b.ToTable($"{TablePrefix}MotorAxes");
            b.ConfigureByConvention();

            b.Property(x => x.Name).IsRequired().HasMaxLength(MotorConsts.MaxNameLength);
            b.Property(x => x.Description).HasMaxLength(MotorConsts.MaxDescriptionLength);
            b.Property(x => x.PortName).IsRequired().HasMaxLength(MotorConsts.MaxPortNameLength);
            b.Property(x => x.Brand).HasConversion<int>();
            b.Property(x => x.Model).HasMaxLength(MotorConsts.MaxBrandLength);
            b.Property(x => x.Status).HasConversion<int>();
            b.Property(x => x.HomeMethod).HasConversion<int>();

            b.HasIndex(x => x.AxisIndex).IsUnique();
            b.HasIndex(x => x.IsEnabled);
            b.HasIndex(x => new { x.PortName, x.SlaveId }).IsUnique();

            // 一根轴拥有多套运动配置（级联删除）
            b.HasMany(x => x.MotionConfigs)
                .WithOne()
                .HasForeignKey(x => x.MotorAxisId)
                .OnDelete(DeleteBehavior.Cascade);

            // 一根轴拥有多条故障记录（级联删除）
            b.HasMany(x => x.FaultRecords)
                .WithOne()
                .HasForeignKey(x => x.MotorAxisId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── 运动配置表 ────────────────────────────────────────────────────────────
        builder.Entity<MotorMotionConfig>(b =>
        {
            b.ToTable($"{TablePrefix}MotorMotionConfigs");
            b.ConfigureByConvention();

            b.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(MotorConsts.MaxMotionConfigNameLength);
            b.Property(x => x.Description).HasMaxLength(MotorConsts.MaxDescriptionLength);

            b.HasIndex(x => x.MotorAxisId);
            b.HasIndex(x => new { x.MotorAxisId, x.IsDefault });
        });

        // ── 故障记录表 ────────────────────────────────────────────────────────────
        builder.Entity<MotorFaultRecord>(b =>
        {
            b.ToTable($"{TablePrefix}MotorFaultRecords");
            b.ConfigureByConvention();

            b.Property(x => x.FaultDescription)
                .IsRequired()
                .HasMaxLength(MotorConsts.MaxDescriptionLength);
            b.Property(x => x.HandlingNote).HasMaxLength(MotorConsts.MaxDescriptionLength);

            b.HasIndex(x => x.MotorAxisId);
            b.HasIndex(x => x.OccurredAt);
            b.HasIndex(x => new { x.MotorAxisId, x.IsHandled });
        });

        // ── PR路径表（雷赛iCL-RS专用） ────────────────────────────────────────────
        builder.Entity<MotorPrPath>(b =>
        {
            b.ToTable($"{TablePrefix}MotorPrPaths");
            b.ConfigureByConvention();

            b.Property(x => x.Name).IsRequired().HasMaxLength(MotorConsts.MaxPrPathNameLength);

            b.HasIndex(x => x.MotorAxisId);
            b.HasIndex(x => new { x.MotorAxisId, x.PathIndex }).IsUnique();
        });
    }
}
