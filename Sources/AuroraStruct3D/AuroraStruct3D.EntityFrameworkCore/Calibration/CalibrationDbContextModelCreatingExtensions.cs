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
            b.Property(x => x.BoardType).HasConversion<int>();
            // 棋盘格标定板参数
            b.Property(x => x.PhysicalSquareSizeMm).HasPrecision(10, 4);
            // 圆点标定板参数（可空，保证历史棋盘格数据兼容）
            b.Property(x => x.CircleSpacingMm).HasPrecision(10, 4).IsRequired(false);
            b.Property(x => x.CircleDiameterMm).HasPrecision(10, 4).IsRequired(false);
            b.Property(x => x.CirclePatternCols).IsRequired(false);
            b.Property(x => x.CirclePatternRows).IsRequired(false);
            b.Property(x => x.HasCenterMarker).IsRequired(false);
            b.Property(x => x.HasCornerLocators).IsRequired(false);
            b.Property(x => x.MarkerRow).IsRequired(false);
            b.Property(x => x.MarkerCol).IsRequired(false);
            b.Property(x => x.CircleDetectorConfigJson).HasMaxLength(4000).IsRequired(false);
            b.Property(x => x.BoundProjectorDeviceId).IsRequired(false);
            b.Property(x => x.MainCameraDeviceId).IsRequired(false);
            b.Property(x => x.SecondaryCameraDeviceId).IsRequired(false);
            b.Property(x => x.MainCameraMotorAxisId).IsRequired(false);
            b.Property(x => x.SecondaryCameraMotorAxisId).IsRequired(false);
            b.Property(x => x.DistanceMotorAxisId).IsRequired(false);
            b.HasIndex(x => x.CalibStatus);
        });

        // ── 电机参数表 ────────────────────────────────────────────────────────────
        builder.Entity<CalibMotorParam>(b =>
        {
            b.ToTable($"{TablePrefix}CalibMotorParams");
            b.ConfigureByConvention();

            b.Property(x => x.OriginDirection).HasConversion<int>();
            b.Property(x => x.GearRatio).HasPrecision(18, 6);
            b.Property(x => x.MechanicalOriginPosition).HasPrecision(18, 6);
            b.Property(x => x.PositiveSoftLimit).HasPrecision(18, 6);
            b.Property(x => x.NegativeSoftLimit).HasPrecision(18, 6);
            b.Property(x => x.HomeSpeed).HasPrecision(18, 6);
            b.Property(x => x.MoveAfterHome).HasDefaultValue(false);
            b.Property(x => x.WithZSignal).HasDefaultValue(false);
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
            b.Property(x => x.CameraPosition)
                .HasMaxLength(CalibConsts.MaxCameraPositionLength)
                .IsRequired(false);

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
            b.Property(x => x.DarkLevel).HasDefaultValue((byte)24);
            b.Property(x => x.BrightLevel).HasDefaultValue((byte)220);
            b.ToTable(
                $"{TablePrefix}CalibProjectorParams",
                tableBuilder => tableBuilder.HasCheckConstraint(
                    "CK_AbpProCalibProjectorParams_GrayLevels",
                    "\"DarkLevel\" >= 0 AND \"BrightLevel\" <= 255 AND \"DarkLevel\" < \"BrightLevel\""));
            b.Property(x => x.ProjectionRatio).HasPrecision(10, 4);
            b.Property(x => x.PhaseShift).HasPrecision(10, 6);
            // Step3 投影仪参数配置页新增字段
            b.Property(x => x.FringeType).IsRequired().HasMaxLength(8).HasDefaultValue("bw");
            b.Property(x => x.PeriodCount).HasDefaultValue(8);

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
        });

        // ── 标定照片记录表（Step 5） ──────────────────────────────────────────────
        builder.Entity<CalibPhotoRecord>(b =>
        {
            b.ToTable($"{TablePrefix}CalibPhotoRecords");
            b.ConfigureByConvention();

            b.Property(x => x.BlobKey).IsRequired().HasMaxLength(CalibConsts.MaxBlobKeyLength);
            b.Property(x => x.PhotoType).HasConversion<int>();
            b.Property(x => x.StereoRole).HasConversion<int>().IsRequired(false);
            b.Property(x => x.ExtrinsicPhase).HasConversion<int>().IsRequired(false);
            b.Property(x => x.ExposureScore).IsRequired(false);
            b.Property(x => x.SharpnessScore).IsRequired(false);
            b.ToTable(
                $"{TablePrefix}CalibPhotoRecords",
                tableBuilder =>
                {
                    tableBuilder.HasCheckConstraint(
                        "CK_AbpProCalibPhotoRecords_ExposureScore",
                        "\"ExposureScore\" IS NULL OR (\"ExposureScore\" >= 1 AND \"ExposureScore\" <= 100)"
                    );
                    tableBuilder.HasCheckConstraint(
                        "CK_AbpProCalibPhotoRecords_SharpnessScore",
                        "\"SharpnessScore\" IS NULL OR (\"SharpnessScore\" >= 1 AND \"SharpnessScore\" <= 100)"
                    );
                }
            );
            // ThumbnailBase64 为 nvarchar(max)，不限制长度

            b.HasIndex(x => x.CalibProjectId);
            b.HasIndex(x => new { x.CalibProjectId, x.CameraDeviceId });
            b.HasIndex(x => new
            {
                x.CalibProjectId,
                x.CameraDeviceId,
                x.PhotoType,
            });
            b.HasIndex(x => new { x.CalibProjectId, x.PairGroupId });
            b.HasIndex(x => x.CapturedAt);
        });

        // ── 双目标定结果表（Step 5 双目联合）────────────────────────────────────
        builder.Entity<CalibStereoResult>(b =>
        {
            b.ToTable($"{TablePrefix}CalibStereoResults");
            b.ConfigureByConvention();

            b.Property(x => x.RotationMatrixJson)
                .IsRequired()
                .HasMaxLength(CalibConsts.MaxCalibResultJsonLength);
            b.Property(x => x.TranslationVectorJson)
                .IsRequired()
                .HasMaxLength(CalibConsts.MaxCalibResultJsonLength);
            b.Property(x => x.TransformLtoRJson)
                .IsRequired()
                .HasMaxLength(CalibConsts.MaxCalibResultJsonLength);
            b.Property(x => x.TransformRtoLJson)
                .IsRequired()
                .HasMaxLength(CalibConsts.MaxCalibResultJsonLength);
            b.Property(x => x.RectificationR1Json)
                .IsRequired()
                .HasMaxLength(CalibConsts.MaxCalibResultJsonLength);
            b.Property(x => x.RectificationR2Json)
                .IsRequired()
                .HasMaxLength(CalibConsts.MaxCalibResultJsonLength);
            b.Property(x => x.ProjectionP1Json)
                .IsRequired()
                .HasMaxLength(CalibConsts.MaxCalibResultJsonLength);
            b.Property(x => x.ProjectionP2Json)
                .IsRequired()
                .HasMaxLength(CalibConsts.MaxCalibResultJsonLength);
            b.Property(x => x.Map1XBlobKey).IsRequired().HasMaxLength(CalibConsts.MaxBlobKeyLength);
            b.Property(x => x.Map1YBlobKey).IsRequired().HasMaxLength(CalibConsts.MaxBlobKeyLength);
            b.Property(x => x.Map2XBlobKey).IsRequired().HasMaxLength(CalibConsts.MaxBlobKeyLength);
            b.Property(x => x.Map2YBlobKey).IsRequired().HasMaxLength(CalibConsts.MaxBlobKeyLength);

            b.HasIndex(x => x.CalibProjectId).IsUnique();
            b.HasIndex(x => new { x.MainCameraDeviceId, x.SecondaryCameraDeviceId });
        });
    }
}
