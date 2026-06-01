using AuroraStruct3D.Cameras;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AuroraStruct3D.EntityFrameworkCore;

/// <summary>
/// 相机模块数据库配置扩展
/// </summary>
public static class CameraDbContextModelCreatingExtensions
{
    private const string TablePrefix = AuroraStruct3DDbProperties.DbTablePrefix;

    /// <summary>
    /// 配置相机相关数据库表结构
    /// </summary>
    public static void ConfigureCamera(this ModelBuilder builder)
    {
        // ── 相机设备表 ────────────────────────────────────────────────────────────
        builder.Entity<CameraDevice>(b =>
        {
            b.ToTable($"{TablePrefix}CameraDevices");
            b.ConfigureByConvention();

            b.Property(x => x.Name).IsRequired().HasMaxLength(CameraConsts.MaxNameLength);

            b.Property(x => x.Model).HasMaxLength(CameraConsts.MaxNameLength);

            b.Property(x => x.Description).HasMaxLength(CameraConsts.MaxDescriptionLength);

            b.Property(x => x.Status).HasConversion<int>();

            b.Property(x => x.ImageRotationAngle).HasDefaultValue(0);

            b.HasIndex(x => x.DeviceIndex).IsUnique();
            b.HasIndex(x => x.IsEnabled);

            // 一个相机拥有多个参数集（级联删除）
            b.HasMany(x => x.ParameterSets)
                .WithOne()
                .HasForeignKey(x => x.CameraDeviceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── 相机参数集表 ──────────────────────────────────────────────────────────
        builder.Entity<CameraParameterSet>(b =>
        {
            b.ToTable($"{TablePrefix}CameraParameterSets");
            b.ConfigureByConvention();

            b.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(CameraConsts.MaxParameterSetNameLength);

            b.Property(x => x.Description).HasMaxLength(CameraConsts.MaxDescriptionLength);

            b.HasIndex(x => x.CameraDeviceId);
            b.HasIndex(x => new { x.CameraDeviceId, x.IsDefault });

            // 一个参数集拥有多个参数项（级联删除）
            b.HasMany(x => x.Parameters)
                .WithOne()
                .HasForeignKey(x => x.ParameterSetId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── 相机参数项表 ──────────────────────────────────────────────────────────
        builder.Entity<CameraParameter>(b =>
        {
            b.ToTable($"{TablePrefix}CameraParameters");
            b.ConfigureByConvention();

            b.Property(x => x.ParamKey)
                .IsRequired()
                .HasMaxLength(CameraConsts.MaxParameterKeyLength);

            b.Property(x => x.Value)
                .IsRequired()
                .HasMaxLength(CameraConsts.MaxParameterValueLength);

            b.Property(x => x.Description).HasMaxLength(CameraConsts.MaxDescriptionLength);

            b.Property(x => x.ParamType).HasConversion<int>();

            b.HasIndex(x => x.ParameterSetId);
            // 同一参数集内参数键不重复
            b.HasIndex(x => new { x.ParameterSetId, x.ParamKey }).IsUnique();
        });

        // ── 相机操作日志表 ─────────────────────────────────────────────────────────
        builder.Entity<CameraOperationLog>(b =>
        {
            b.ToTable($"{TablePrefix}CameraOperationLogs");
            b.ConfigureByConvention();

            b.Property(x => x.ParameterSummary)
                .HasMaxLength(CameraConsts.MaxOperationParameterSummaryLength);

            b.Property(x => x.ErrorMessage)
                .HasMaxLength(CameraConsts.MaxOperationLogErrorMessageLength);

            b.Property(x => x.OperationType).HasConversion<int>();

            // 外键：操作日志从属于相机设备，相机删除时级联删除日志
            b.HasOne<CameraDevice>()
                .WithMany()
                .HasForeignKey(x => x.CameraDeviceId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(x => x.CameraDeviceId);
            b.HasIndex(x => x.OccurredAt);
            b.HasIndex(x => new { x.CameraDeviceId, x.OccurredAt });
            b.HasIndex(x => new { x.CameraDeviceId, x.IsSuccess });
            b.HasIndex(x => new { x.CameraDeviceId, x.OperationType });
        });
    }
}
