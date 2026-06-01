using AuroraStruct3D.CalibrationManagement;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AuroraStruct3D.EntityFrameworkCore;

/// <summary>
/// 标定管理模块数据库配置扩展。
/// 表名格式：{<c>AuroraStruct3DDbProperties.DbTablePrefix</c>}{<c>CalibrationManagementConsts.ModuleTableSegment</c>}{实体复数名}
/// 如：AbpProCalibDevices、AbpProCalibProjects 。
/// 跨聚合外键（CameraDeviceId / MotorAxisId / ProjectorDeviceId）不在 EF 模型层建立物理外键约束，
/// 避免与现有 Camera/Motor/Projector 聚合产生级联耦合，由应用层保证一致性。
/// </summary>
public static class CalibrationDbContextModelCreatingExtensions
{
    private const string TablePrefix =
        AuroraStruct3DDbProperties.DbTablePrefix + CalibrationManagementConsts.ModuleTableSegment;

    /// <summary>配置标定管理相关数据库表结构</summary>
    public static void ConfigureCalibrationManagement(this ModelBuilder builder)
    {
        // ── 标定设备聚合根 ───────────────────────────────────────────────────
        builder.Entity<CalibrationDevice>(b =>
        {
            b.ToTable($"{TablePrefix}Devices");
            b.ConfigureByConvention();

            b.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(CalibrationManagementConsts.MaxNameLength);
            b.Property(x => x.Description)
                .HasMaxLength(CalibrationManagementConsts.MaxDescriptionLength);
            b.Property(x => x.DeviceType).HasConversion<int>().IsRequired();
            b.Property(x => x.IsActive).IsRequired();

            b.HasIndex(x => x.Name);
            b.HasIndex(x => x.DeviceType);
            b.HasIndex(x => x.IsActive);

            // 子集合：相机/电机/投射器绑定、云台组、互锁规则、各参数快照
            b.HasMany(x => x.CameraBindings)
                .WithOne()
                .HasForeignKey(x => x.CalibrationDeviceId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasMany(x => x.MotorBindings)
                .WithOne()
                .HasForeignKey(x => x.CalibrationDeviceId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasMany(x => x.ProjectorBindings)
                .WithOne()
                .HasForeignKey(x => x.CalibrationDeviceId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasMany(x => x.GimbalGroups)
                .WithOne()
                .HasForeignKey(x => x.CalibrationDeviceId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasMany(x => x.InterlockRules)
                .WithOne()
                .HasForeignKey(x => x.CalibrationDeviceId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasMany(x => x.CameraParameters)
                .WithOne()
                .HasForeignKey(x => x.CalibrationDeviceId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasMany(x => x.ProjectorParameters)
                .WithOne()
                .HasForeignKey(x => x.CalibrationDeviceId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasMany(x => x.MotorParameters)
                .WithOne()
                .HasForeignKey(x => x.CalibrationDeviceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── 相机绑定 ────────────────────────────────────────────────────────
        builder.Entity<CalibrationCameraBinding>(b =>
        {
            b.ToTable($"{TablePrefix}CameraBindings");
            b.ConfigureByConvention();

            b.Property(x => x.Role).HasConversion<int>().IsRequired();

            b.HasIndex(x => x.CalibrationDeviceId);
            b.HasIndex(x => x.CameraDeviceId);
            // 同一设备下角色唯一
            b.HasIndex(x => new { x.CalibrationDeviceId, x.Role }).IsUnique();
        });

        // ── 电机绑定 ────────────────────────────────────────────────────────
        builder.Entity<CalibrationMotorBinding>(b =>
        {
            b.ToTable($"{TablePrefix}MotorBindings");
            b.ConfigureByConvention();

            b.Property(x => x.Role).HasConversion<int>().IsRequired();

            b.HasIndex(x => x.CalibrationDeviceId);
            b.HasIndex(x => x.MotorAxisId);
            b.HasIndex(x => x.GimbalGroupId);
        });

        // ── 投射器绑定 ──────────────────────────────────────────────────────
        builder.Entity<CalibrationProjectorBinding>(b =>
        {
            b.ToTable($"{TablePrefix}ProjectorBindings");
            b.ConfigureByConvention();

            b.Property(x => x.Role).HasConversion<int>().IsRequired();

            b.HasIndex(x => x.CalibrationDeviceId);
            b.HasIndex(x => x.ProjectorDeviceId);
        });

        // ── 云台组 ──────────────────────────────────────────────────────────
        builder.Entity<CalibrationGimbalGroup>(b =>
        {
            b.ToTable($"{TablePrefix}GimbalGroups");
            b.ConfigureByConvention();

            b.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(CalibrationManagementConsts.MaxNameLength);

            b.HasIndex(x => x.CalibrationDeviceId);
            b.HasIndex(x => new { x.CalibrationDeviceId, x.Name }).IsUnique();

            b.HasMany(x => x.PresetPositions)
                .WithOne()
                .HasForeignKey(x => x.GimbalGroupId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── 云台预设位置 ────────────────────────────────────────────────────
        builder.Entity<CalibrationGimbalPreset>(b =>
        {
            b.ToTable($"{TablePrefix}GimbalPresets");
            b.ConfigureByConvention();

            b.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(CalibrationManagementConsts.MaxNameLength);
            b.Property(x => x.Remarks)
                .HasMaxLength(CalibrationManagementConsts.MaxDescriptionLength);

            b.HasIndex(x => x.GimbalGroupId);
        });

        // ── 联动软限位规则 ──────────────────────────────────────────────────
        builder.Entity<CalibrationMotorInterlockRule>(b =>
        {
            b.ToTable($"{TablePrefix}MotorInterlockRules");
            b.ConfigureByConvention();

            b.Property(x => x.BlockedDirection).HasConversion<int>().IsRequired();
            b.Property(x => x.Description)
                .HasMaxLength(CalibrationManagementConsts.MaxDescriptionLength);

            b.HasIndex(x => x.CalibrationDeviceId);
            b.HasIndex(x => x.SourceMotorAxisId);
            b.HasIndex(x => x.TargetMotorAxisId);
        });

        // ── 相机硬件参数 ───────────────────────────────────────────────────
        builder.Entity<CalibrationCameraParameter>(b =>
        {
            b.ToTable($"{TablePrefix}CameraParameters");
            b.ConfigureByConvention();

            b.Property(x => x.Role).HasConversion<int>().IsRequired();
            b.Property(x => x.CmosSize).HasConversion<int>().IsRequired();

            b.HasIndex(x => x.CalibrationDeviceId);
            b.HasIndex(x => x.CameraDeviceId);
            b.HasIndex(x => new { x.CalibrationDeviceId, x.CameraDeviceId }).IsUnique();
        });

        // ── 投射器硬件参数 ─────────────────────────────────────────────────
        builder.Entity<CalibrationProjectorParameter>(b =>
        {
            b.ToTable($"{TablePrefix}ProjectorParameters");
            b.ConfigureByConvention();

            b.Property(x => x.Pattern).HasConversion<int>().IsRequired();

            b.HasIndex(x => x.CalibrationDeviceId);
            b.HasIndex(x => x.ProjectorDeviceId);
            b.HasIndex(x => new { x.CalibrationDeviceId, x.ProjectorDeviceId }).IsUnique();
        });

        // ── 电机参数 ───────────────────────────────────────────────────────
        builder.Entity<CalibrationMotorParameter>(b =>
        {
            b.ToTable($"{TablePrefix}MotorParameters");
            b.ConfigureByConvention();

            b.Property(x => x.Role).HasConversion<int>().IsRequired();

            b.HasIndex(x => x.CalibrationDeviceId);
            b.HasIndex(x => x.MotorAxisId);
            b.HasIndex(x => new { x.CalibrationDeviceId, x.MotorAxisId }).IsUnique();
        });

        // ── 标定工程聚合根 ─────────────────────────────────────────────────
        builder.Entity<CalibrationProject>(b =>
        {
            b.ToTable($"{TablePrefix}Projects");
            b.ConfigureByConvention();

            b.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(CalibrationManagementConsts.MaxNameLength);
            b.Property(x => x.Description)
                .HasMaxLength(CalibrationManagementConsts.MaxDescriptionLength);
            b.Property(x => x.Status).HasConversion<int>().IsRequired();
            b.Property(x => x.FailureReason)
                .HasMaxLength(CalibrationManagementConsts.MaxErrorMessageLength);

            b.Property(x => x.BoardType).HasConversion<int>().IsRequired();
            b.Property(x => x.AprilTagFamily)
                .HasMaxLength(CalibrationManagementConsts.MaxAprilTagFamilyLength);

            b.Property(x => x.ImageFormat).HasConversion<int>().IsRequired();
            b.Property(x => x.UnifiedWhiteBalance)
                .HasMaxLength(CalibrationManagementConsts.MaxRoleLabelLength);

            b.HasIndex(x => x.CalibrationDeviceId);
            b.HasIndex(x => x.Status);
            b.HasIndex(x => x.CreationTime);

            b.HasMany(x => x.Frames)
                .WithOne()
                .HasForeignKey(x => x.CalibrationProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── 采集帧 ─────────────────────────────────────────────────────────
        builder.Entity<CalibrationCaptureFrame>(b =>
        {
            b.ToTable($"{TablePrefix}CaptureFrames");
            b.ConfigureByConvention();

            b.Property(x => x.RejectionReason)
                .HasMaxLength(CalibrationManagementConsts.MaxDescriptionLength);

            b.HasIndex(x => x.CalibrationProjectId);
            b.HasIndex(x => new { x.CalibrationProjectId, x.FrameIndex }).IsUnique();

            b.HasMany(x => x.Images)
                .WithOne()
                .HasForeignKey(x => x.CalibrationCaptureFrameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── 采集图像 ───────────────────────────────────────────────────────
        builder.Entity<CalibrationCaptureImage>(b =>
        {
            b.ToTable($"{TablePrefix}CaptureImages");
            b.ConfigureByConvention();

            b.Property(x => x.BlobName)
                .IsRequired()
                .HasMaxLength(CalibrationManagementConsts.MaxBlobNameLength);
            b.Property(x => x.CameraRole).HasConversion<int>().IsRequired();

            b.HasIndex(x => x.CalibrationCaptureFrameId);
            b.HasIndex(x => x.CameraDeviceId);
        });

        // ── 标定结果聚合根 ─────────────────────────────────────────────────
        builder.Entity<CalibrationResult>(b =>
        {
            b.ToTable($"{TablePrefix}Results");
            b.ConfigureByConvention();

            b.Property(x => x.CameraIntrinsicsJson).IsRequired().HasColumnType("text");
            b.Property(x => x.CameraExtrinsicsJson).IsRequired().HasColumnType("text");
            b.Property(x => x.StructuredLightCalibrationJson).HasColumnType("text");

            b.HasIndex(x => x.CalibrationProjectId);
            b.HasIndex(x => x.CalibrationDeviceId);
            b.HasIndex(x => new { x.CalibrationProjectId, x.Version }).IsUnique();
            b.HasIndex(x => new { x.CalibrationDeviceId, x.IsActive });

            b.HasMany(x => x.Validations)
                .WithOne()
                .HasForeignKey(x => x.CalibrationResultId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── 验证记录 ───────────────────────────────────────────────────────
        builder.Entity<CalibrationValidationRecord>(b =>
        {
            b.ToTable($"{TablePrefix}ValidationRecords");
            b.ConfigureByConvention();

            b.Property(x => x.ValidationType).HasConversion<int>().IsRequired();
            b.Property(x => x.MetricsJson).HasColumnType("text");
            b.Property(x => x.ReportBlobName)
                .HasMaxLength(CalibrationManagementConsts.MaxBlobNameLength);
            b.Property(x => x.Remarks)
                .HasMaxLength(CalibrationManagementConsts.MaxDescriptionLength);

            b.HasIndex(x => x.CalibrationResultId);
            b.HasIndex(x => x.ValidationType);
        });

        // ── 相机参数模板 ───────────────────────────────────────────────────
        builder.Entity<CalibrationCameraTemplate>(b =>
        {
            b.ToTable($"{TablePrefix}CameraTemplates");
            b.ConfigureByConvention();

            b.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(CalibrationManagementConsts.MaxNameLength);
            b.Property(x => x.CameraModel).HasMaxLength(CalibrationManagementConsts.MaxNameLength);
            b.Property(x => x.Description)
                .HasMaxLength(CalibrationManagementConsts.MaxDescriptionLength);
            b.Property(x => x.ParametersJson).IsRequired().HasColumnType("text");

            b.HasIndex(x => x.Name);
            b.HasIndex(x => x.CameraModel);
        });

        // ── 结构光参数模板 ─────────────────────────────────────────────────
        builder.Entity<CalibrationProjectorTemplate>(b =>
        {
            b.ToTable($"{TablePrefix}ProjectorTemplates");
            b.ConfigureByConvention();

            b.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(CalibrationManagementConsts.MaxNameLength);
            b.Property(x => x.ProjectorModel)
                .HasMaxLength(CalibrationManagementConsts.MaxNameLength);
            b.Property(x => x.Description)
                .HasMaxLength(CalibrationManagementConsts.MaxDescriptionLength);
            b.Property(x => x.ParametersJson).IsRequired().HasColumnType("text");

            b.HasIndex(x => x.Name);
            b.HasIndex(x => x.ProjectorModel);
        });
    }
}
