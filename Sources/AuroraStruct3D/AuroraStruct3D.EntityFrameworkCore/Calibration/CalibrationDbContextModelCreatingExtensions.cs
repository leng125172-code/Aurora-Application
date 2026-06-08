using AuroraStruct3D.Calibration;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AuroraStruct3D.EntityFrameworkCore;

/// <summary>
/// 标定模块数据库配置扩展
/// </summary>
public static class CalibrationDbContextModelCreatingExtensions
{
    private const string TablePrefix = AuroraStruct3DDbProperties.DbTablePrefix;

    /// <summary>
    /// 配置标定相关数据库表结构
    /// </summary>
    public static void ConfigureCalibration(this ModelBuilder builder)
    {
        // ── 标定项目表（聚合根） ───────────────────────────────────────────────────
        builder.Entity<CalibProject>(b =>
        {
            b.ToTable($"{TablePrefix}CalibProjects");
            b.ConfigureByConvention();

            b.Property(x => x.Name).IsRequired().HasMaxLength(CalibConsts.MaxNameLength);
            b.Property(x => x.Description).HasMaxLength(CalibConsts.MaxDescriptionLength);
            b.Property(x => x.DeviceSeries).HasConversion<int>();
            b.Property(x => x.DeviceType).HasConversion<int>();
            b.Property(x => x.CalibStatus).HasConversion<int>();
            // 棋盘格标定板参数
            b.Property(x => x.PhysicalSquareSizeMm).HasPrecision(10, 4);
            b.HasIndex(x => x.CalibStatus);
        });

        // ── 电机参数表 ────────────────────────────────────────────────────────────
        builder.Entity<CalibMotorParam>(b =>
        {
            b.ToTable($"{TablePrefix}CalibMotorParams");
            b.ConfigureByConvention();

            b.Property(x => x.MotorType).HasConversion<int>();
            b.Property(x => x.OriginDirection).HasConversion<int>();
            b.Property(x => x.GearRatio).HasPrecision(18, 6);
            b.Property(x => x.MechanicalOriginPosition).HasPrecision(18, 6);
            b.Property(x => x.PositiveSoftLimit).HasPrecision(18, 6);
            b.Property(x => x.NegativeSoftLimit).HasPrecision(18, 6);
            b.Property(x => x.HomeSpeed).HasPrecision(18, 6);
            b.Property(x => x.HomeAcceleration).HasPrecision(18, 6);

            b.HasIndex(x => x.CalibProjectId);
            // 同一项目中，同一根电机轴仅允许一条参数记录
            b.HasIndex(x => new { x.CalibProjectId, x.MotorAxisId }).IsUnique();
        });

        // ── 电机联动限制表 ────────────────────────────────────────────────────────
        builder.Entity<CalibMotorConstraint>(b =>
        {
            b.ToTable($"{TablePrefix}CalibMotorConstraints");
            b.ConfigureByConvention();

            b.Property(x => x.ForbiddenDirection).HasConversion<int>();
            b.Property(x => x.PositionRangeMin).HasPrecision(18, 6);
            b.Property(x => x.PositionRangeMax).HasPrecision(18, 6);
            b.Property(x => x.RuleDescription).HasMaxLength(CalibConsts.MaxRuleDescriptionLength);

            b.HasIndex(x => x.CalibProjectId);
            b.HasIndex(x => x.IsEnabled);
        });

        // ── 云台组表 ──────────────────────────────────────────────────────────────
        builder.Entity<CalibGimbalGroup>(b =>
        {
            b.ToTable($"{TablePrefix}CalibGimbalGroups");
            b.ConfigureByConvention();

            b.Property(x => x.Name).IsRequired().HasMaxLength(CalibConsts.MaxNameLength);
            b.Property(x => x.Description).HasMaxLength(CalibConsts.MaxDescriptionLength);
            b.Property(x => x.MaxSpeed).HasPrecision(18, 6);
            b.Property(x => x.Acceleration).HasPrecision(18, 6);
            b.Property(x => x.AccelerationTime).HasPrecision(18, 3);
            b.Property(x => x.DecelerationTime).HasPrecision(18, 3);

            b.HasIndex(x => x.IsEnabled);

            // 云台组与绑定轴：一对多，级联删除
            b.HasMany(x => x.Bindings)
                .WithOne()
                .HasForeignKey(x => x.GimbalGroupId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── 云台绑定表 ────────────────────────────────────────────────────────────
        builder.Entity<CalibGimbalBinding>(b =>
        {
            b.ToTable($"{TablePrefix}CalibGimbalBindings");
            b.ConfigureByConvention();

            b.Property(x => x.AxisType).HasConversion<int>();

            b.HasIndex(x => x.GimbalGroupId);
            // 同一云台组内，每种轴类型仅允许绑定一个电机
            b.HasIndex(x => new { x.GimbalGroupId, x.AxisType }).IsUnique();
        });

        // ── 相机参数表 ────────────────────────────────────────────────────────────
        builder.Entity<CalibCameraParam>(b =>
        {
            b.ToTable($"{TablePrefix}CalibCameraParams");
            b.ConfigureByConvention();

            b.Property(x => x.Name).IsRequired().HasMaxLength(CalibConsts.MaxNameLength);
            b.Property(x => x.Description).HasMaxLength(CalibConsts.MaxDescriptionLength);
            b.Property(x => x.SensorSize)
                .IsRequired()
                .HasMaxLength(CalibConsts.MaxSensorSizeLength);
            b.Property(x => x.TemplateCategory).HasMaxLength(CalibConsts.MaxTemplateCategoryLength);
            b.Property(x => x.SensorWidthMm).HasPrecision(10, 4);
            b.Property(x => x.SensorHeightMm).HasPrecision(10, 4);
            b.Property(x => x.PixelSizeUm).HasPrecision(10, 4);
            b.Property(x => x.LensFocalLength).HasPrecision(10, 4);
            b.Property(x => x.MaxAperture).HasPrecision(8, 2);
            b.Property(x => x.MinAperture).HasPrecision(8, 2);
            b.Property(x => x.CurrentAperture).HasPrecision(8, 2);
            b.Property(x => x.GainMinDb).HasPrecision(8, 2);
            b.Property(x => x.GainMaxDb).HasPrecision(8, 2);

            b.HasIndex(x => x.CalibProjectId);
            b.HasIndex(x => x.CameraDeviceId);
            b.HasIndex(x => new { x.IsTemplateMode, x.TemplateCategory });

            // 标定计算结果字段
            b.Property(x => x.IntrinsicMatrixJson)
                .HasMaxLength(CalibConsts.MaxCalibResultJsonLength);
            b.Property(x => x.DistCoeffsJson).HasMaxLength(CalibConsts.MaxCalibResultJsonLength);
            b.Property(x => x.ExtrinsicRvecJson).HasMaxLength(CalibConsts.MaxCalibResultJsonLength);
            b.Property(x => x.ExtrinsicTvecJson).HasMaxLength(CalibConsts.MaxCalibResultJsonLength);
        });

        // ── 结构光参数表 ──────────────────────────────────────────────────────────
        builder.Entity<CalibProjectorParam>(b =>
        {
            b.ToTable($"{TablePrefix}CalibProjectorParams");
            b.ConfigureByConvention();

            b.Property(x => x.Name).IsRequired().HasMaxLength(CalibConsts.MaxNameLength);
            b.Property(x => x.Description).HasMaxLength(CalibConsts.MaxDescriptionLength);
            b.Property(x => x.TemplateCategory).HasMaxLength(CalibConsts.MaxTemplateCategoryLength);
            b.Property(x => x.PatternType).HasConversion<int>();
            b.Property(x => x.ProjectionRatio).HasPrecision(10, 4);
            b.Property(x => x.PhaseShift).HasPrecision(10, 6);

            b.HasIndex(x => x.CalibProjectId);
            b.HasIndex(x => x.ProjectorDeviceId);
            b.HasIndex(x => new { x.IsTemplateMode, x.TemplateCategory });
        });

        // ── 设备绑定表 ────────────────────────────────────────────────────────────
        builder.Entity<CalibDeviceBinding>(b =>
        {
            b.ToTable($"{TablePrefix}CalibDeviceBindings");
            b.ConfigureByConvention();

            b.Property(x => x.BindingType).HasConversion<int>();
            b.Property(x => x.TargetRole)
                .IsRequired()
                .HasMaxLength(CalibConsts.MaxTargetRoleLength);
            b.Property(x => x.BindingStatus).HasConversion<int>();
            b.Property(x => x.StatusMessage).HasMaxLength(CalibConsts.MaxStatusMessageLength);

            b.HasIndex(x => x.CalibProjectId);
            b.HasIndex(x => new { x.CalibProjectId, x.BindingType });
            // 同一项目内，目标角色唯一（每个逻辑角色只能绑定一个设备）
            b.HasIndex(x => new { x.CalibProjectId, x.TargetRole }).IsUnique();

            // 云台组关联（三目设备电机绑定时使用，Restrict：云台组被引用时不允许直接删除）
            b.HasOne<CalibGimbalGroup>()
                .WithMany()
                .HasForeignKey(x => x.BoundGimbalGroupId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
        });

        // ── 标定照片记录表（Step 5） ──────────────────────────────────────────────
        builder.Entity<CalibPhotoRecord>(b =>
        {
            b.ToTable($"{TablePrefix}CalibPhotoRecords");
            b.ConfigureByConvention();

            b.Property(x => x.BlobKey).IsRequired().HasMaxLength(CalibConsts.MaxBlobKeyLength);
            b.Property(x => x.PhotoType).HasConversion<int>();
            // ThumbnailBase64 为 nvarchar(max)，不限制长度

            b.HasIndex(x => x.CalibProjectId);
            b.HasIndex(x => new { x.CalibProjectId, x.CameraDeviceId });
            b.HasIndex(x => new
            {
                x.CalibProjectId,
                x.CameraDeviceId,
                x.PhotoType,
            });
            b.HasIndex(x => x.CapturedAt);
        });
    }
}
