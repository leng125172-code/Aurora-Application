using AuroraStruct3D.Motors;
using AuroraStruct3D.SerialPorts;
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
            b.Property(x => x.Brand).HasConversion<int>();
            b.Property(x => x.Model).HasMaxLength(MotorConsts.MaxBrandLength);
            b.Property(x => x.Status).HasConversion<int>();
            b.Property(x => x.HomeMethod).HasConversion<int>();
            b.Property(x => x.MinRotationAngle);
            b.Property(x => x.MaxRotationAngle);

            // KTECH 设备识别信息（在线读取后缓存）
            b.Property(x => x.KtechDeviceTypeCode);
            b.Property(x => x.KtechDriverName).HasMaxLength(MotorConsts.MaxKtechIdentifierLength);
            b.Property(x => x.KtechMotorName).HasMaxLength(MotorConsts.MaxKtechIdentifierLength);
            b.Property(x => x.KtechChipId).HasMaxLength(MotorConsts.MaxKtechIdentifierLength);
            b.Property(x => x.KtechHardwareVersion).HasMaxLength(MotorConsts.MaxKtechVersionLength);
            b.Property(x => x.KtechMotorVersion).HasMaxLength(MotorConsts.MaxKtechVersionLength);
            b.Property(x => x.KtechFirmwareVersion).HasMaxLength(MotorConsts.MaxKtechVersionLength);

            b.HasIndex(x => x.AxisIndex).IsUnique();
            b.HasIndex(x => x.IsEnabled);
            // 同一总线（SerialPortConfig）上的从机地址必须全局唯一
            b.HasIndex(x => new { x.SerialPortConfigId, x.SlaveId }).IsUnique();

            // 多个电机轴共享同一串口配置（Restrict：串口被引用时不允许直接删除）
            b.HasOne(x => x.SerialPortConfig)
                .WithMany()
                .HasForeignKey(x => x.SerialPortConfigId)
                .OnDelete(DeleteBehavior.Restrict);

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

        // ── 电机操作日志表 ────────────────────────────────────────────────────────
        builder.Entity<MotorOperationLog>(b =>
        {
            b.ToTable($"{TablePrefix}MotorOperationLogs");
            b.ConfigureByConvention();

            b.Property(x => x.CommandCode).HasMaxLength(MotorConsts.MaxCommandCodeLength);
            b.Property(x => x.ParameterSummary)
                .HasMaxLength(MotorConsts.MaxOperationParameterSummaryLength);
            b.Property(x => x.ErrorMessage)
                .HasMaxLength(MotorConsts.MaxOperationLogErrorMessageLength);
            b.Property(x => x.OperationType).HasConversion<int>();

            // 外键关联（轴删除时级联删除其全部操作日志）
            b.HasOne<MotorAxis>()
                .WithMany()
                .HasForeignKey(x => x.MotorAxisId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(x => x.MotorAxisId);
            b.HasIndex(x => x.OccurredAt);
            b.HasIndex(x => new { x.MotorAxisId, x.OccurredAt });
            b.HasIndex(x => new { x.MotorAxisId, x.IsSuccess });
            b.HasIndex(x => new { x.MotorAxisId, x.OperationType });
        });
    }
}
