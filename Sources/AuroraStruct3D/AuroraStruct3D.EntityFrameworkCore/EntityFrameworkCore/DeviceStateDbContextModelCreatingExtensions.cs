using AuroraStruct3D.DeviceState;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AuroraStruct3D.EntityFrameworkCore;

/// <summary>
/// 设备状态模块数据库配置扩展
/// </summary>
public static class DeviceStateDbContextModelCreatingExtensions
{
    private const string TablePrefix = AuroraStruct3DDbProperties.DbTablePrefix;

    /// <summary>
    /// 配置设备状态相关数据库表结构
    /// </summary>
    public static void ConfigureDeviceState(this ModelBuilder builder)
    {
        // ── 设备状态切换日志表 ──────────────────────────────────────────────────────
        builder.Entity<DeviceStateLog>(b =>
        {
            b.ToTable($"{TablePrefix}DeviceStateLogs");
            b.ConfigureByConvention();

            b.Property(x => x.OccurredAt).IsRequired();

            // 状态变化字段
            b.Property(x => x.PreviousStatus).HasConversion<int?>();
            b.Property(x => x.NewStatus).IsRequired().HasConversion<int>();
            b.Property(x => x.IsStatusChange).IsRequired();
            b.Property(x => x.IsTransitionState).IsRequired();

            // 模式变化字段
            b.Property(x => x.PreviousMode).HasConversion<int?>();
            b.Property(x => x.NewMode).IsRequired().HasConversion<int>();
            b.Property(x => x.IsModeChange).IsRequired();

            // 触发信息字段
            b.Property(x => x.Trigger).IsRequired().HasConversion<int>();
            b.Property(x => x.FaultId);

            // 操作人信息字段
            b.Property(x => x.OperatorId).HasMaxLength(DeviceStateConsts.MaxOperatorIdLength);
            b.Property(x => x.OperatorName).HasMaxLength(DeviceStateConsts.MaxOperatorNameLength);

            // 原因说明字段
            b.Property(x => x.Reason).HasMaxLength(DeviceStateConsts.MaxReasonLength);
            b.Property(x => x.Remark).HasMaxLength(DeviceStateConsts.MaxRemarkLength);

            // 执行结果字段
            b.Property(x => x.DurationMs);
            b.Property(x => x.IsSuccessful).IsRequired();
            b.Property(x => x.ErrorMessage).HasMaxLength(DeviceStateConsts.MaxErrorMessageLength);

            // 索引：按时间倒序查询（最常用）
            b.HasIndex(x => x.OccurredAt);
            b.HasIndex(x => x.Trigger);
            b.HasIndex(x => x.NewStatus);
            b.HasIndex(x => x.IsStatusChange);
            b.HasIndex(x => x.IsModeChange);
            b.HasIndex(x => x.FaultId);
        });

        // ── 设备故障记录表 ─────────────────────────────────────────────────────────
        builder.Entity<DeviceFault>(b =>
        {
            b.ToTable($"{TablePrefix}DeviceFaults");
            b.ConfigureByConvention();

            // 故障发生信息
            b.Property(x => x.OccurredAt).IsRequired();
            b.Property(x => x.FaultLevel).IsRequired().HasConversion<int>();
            b.Property(x => x.FaultCode).HasMaxLength(DeviceStateConsts.MaxFaultCodeLength);
            b.Property(x => x.FaultMessage).HasMaxLength(DeviceStateConsts.MaxFaultMessageLength);
            b.Property(x => x.FaultReason).HasMaxLength(DeviceStateConsts.MaxFaultReasonLength);
            b.Property(x => x.Source).IsRequired().HasConversion<int>();
            b.Property(x => x.Fingerprint).HasMaxLength(256);
            b.Property(x => x.DeviceName).HasMaxLength(128);
            b.Property(x => x.WorkflowProjectName).HasMaxLength(128);
            b.Property(x => x.WorkflowName).HasMaxLength(128);
            b.Property(x => x.WorkflowNodeId).HasMaxLength(128);
            b.Property(x => x.LastOccurredAt).IsRequired();
            b.Property(x => x.OccurrenceCount).IsRequired();

            // 处理状态
            b.Property(x => x.IsResolved).IsRequired();
            b.Property(x => x.IsAutoRecovered).IsRequired();

            // 处理信息
            b.Property(x => x.ResolverId).HasMaxLength(DeviceStateConsts.MaxResolverIdLength);
            b.Property(x => x.ResolverName).HasMaxLength(DeviceStateConsts.MaxResolverNameLength);
            b.Property(x => x.ResolvedAt);
            b.Property(x => x.ResolutionDescription)
                .HasMaxLength(DeviceStateConsts.MaxResolutionDescriptionLength);
            b.Property(x => x.DurationMs);

            // 模式影响
            b.Property(x => x.CausedModeSwitch).IsRequired();
            b.Property(x => x.SwitchedToMode).HasConversion<int?>();

            // 关联与备注
            b.Property(x => x.StateLogId);
            b.Property(x => x.Remark).HasMaxLength(DeviceStateConsts.MaxRemarkLength);

            // 索引
            b.HasIndex(x => x.OccurredAt);
            b.HasIndex(x => x.FaultLevel);
            b.HasIndex(x => x.IsResolved);
            b.HasIndex(x => new { x.IsResolved, x.FaultLevel });
            b.HasIndex(x => new { x.IsResolved, x.Fingerprint });
        });
    }
}
