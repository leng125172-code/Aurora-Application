using AuroraStruct3D.Projectors;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AuroraStruct3D.EntityFrameworkCore;

/// <summary>
/// DLP 投影仪模块数据库配置扩展
/// </summary>
public static class ProjectorDbContextModelCreatingExtensions
{
    private const string TablePrefix = AuroraStruct3DDbProperties.DbTablePrefix;

    /// <summary>
    /// 配置 DLP 投影仪相关数据库表结构
    /// </summary>
    public static void ConfigureProjector(this ModelBuilder builder)
    {
        // ── 投影仪设备表 ──────────────────────────────────────────────────────────
        builder.Entity<ProjectorDevice>(b =>
        {
            b.ToTable($"{TablePrefix}Projectors");
            b.ConfigureByConvention();

            // 基本信息
            b.Property(x => x.Name).IsRequired().HasMaxLength(ProjectorConsts.MaxNameLength);
            b.Property(x => x.Description).HasMaxLength(ProjectorConsts.MaxDescriptionLength);

            // 连接方式
            b.Property(x => x.ConnectionType).HasConversion<int>().IsRequired();

            // TCP 连接配置（可空，仅 TCP 模式有效）
            b.Property(x => x.IpAddress).HasMaxLength(ProjectorConsts.MaxIpAddressLength);

            // USB HID 连接配置（仅 USB HID 模式有效；VID/PID 由硬件固定，不存储于数据库）
            b.Property(x => x.HidDeviceIndex);

            // 枚举存储为整数
            b.Property(x => x.ConnectionStatus).HasConversion<int>();
            b.Property(x => x.LedStatus).HasConversion<int>();

            // Phase 3 新增：高级控制状态字段
            // HasSentinel(-1)：仅当属性值为 -1 时才使用数据库默认，避免 CLR 默认值（0）与数据库默认值冲突的警告
            b.Property(x => x.LastColor)
                .HasConversion<int>()
                .HasDefaultValue(ProjectorColor.White)
                // ProjectorColor 底层为 byte，255 不属于任何有效枚举成员，用作哨兵以区分"未赋值"与 CLR 默认值（Red=0）
                .HasSentinel((ProjectorColor)255);
            b.Property(x => x.FlipMode)
                .HasConversion<int>()
                .HasDefaultValue(ProjectorFlipMode.None);
            b.Property(x => x.TriggerMode)
                .HasConversion<int>()
                .HasDefaultValue(ProjectorTriggerMode.Normal);
            b.Property(x => x.BootImage)
                .HasConversion<int>()
                .HasDefaultValue(ProjectorBootImage.Cross)
                .HasSentinel((ProjectorBootImage)(-1));
            b.Property(x => x.CheckerboardPixelSize).HasDefaultValue(30);
            b.Property(x => x.LedRgbR).HasDefaultValue((byte)75);
            b.Property(x => x.LedRgbG).HasDefaultValue((byte)75);
            b.Property(x => x.LedRgbB).HasDefaultValue((byte)75);

            // 索引
            b.HasIndex(x => x.DeviceIndex).IsUnique();
            b.HasIndex(x => x.ConnectionType);
            // TCP 模式：IpAddress 全局唯一（PostgreSQL 部分索引）
            b.HasIndex(x => x.IpAddress)
                .IsUnique()
                .HasFilter($"\"{nameof(ProjectorDevice.IpAddress)}\" IS NOT NULL");
            // HID 模式：同一系统内 HidDeviceIndex 全局唯一
            b.HasIndex(x => x.HidDeviceIndex)
                .IsUnique()
                .HasFilter($"\"{nameof(ProjectorDevice.ConnectionType)}\" = 1");
            b.HasIndex(x => x.IsEnabled);
            b.HasIndex(x => x.ConnectionStatus);

            // 一台投影仪拥有多条操作日志（级联删除）
            b.HasMany(x => x.OperationLogs)
                .WithOne()
                .HasForeignKey(x => x.ProjectorDeviceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── 投影仪操作日志表 ──────────────────────────────────────────────────────
        builder.Entity<ProjectorOperationLog>(b =>
        {
            b.ToTable($"{TablePrefix}ProjectorOperationLogs");
            b.ConfigureByConvention();

            b.Property(x => x.RawCommand).HasMaxLength(64);
            b.Property(x => x.ParameterSummary).HasMaxLength(128);
            b.Property(x => x.ErrorMessage).HasMaxLength(ProjectorConsts.MaxLogMessageLength);

            // 枚举存储为整数
            b.Property(x => x.OperationType).HasConversion<int>();

            // 按投影仪 + 时间查询是最常见的访问模式
            b.HasIndex(x => x.ProjectorDeviceId);
            b.HasIndex(x => x.OccurredAt);
            b.HasIndex(x => new { x.ProjectorDeviceId, x.OccurredAt });
            b.HasIndex(x => new { x.ProjectorDeviceId, x.IsSuccess });
            b.HasIndex(x => new { x.ProjectorDeviceId, x.OperationType });
        });
    }
}
