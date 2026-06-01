using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace AuroraStruct3D.CalibrationManagement.Projects.Dtos;

// ═══════════════════════════════════════════════════════════════════════════
// 工程输出 DTO
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>标定工程列表项 DTO</summary>
public class CalibrationProjectListDto : FullAuditedEntityDto<Guid>
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid CalibrationDeviceId { get; set; }
    public CalibrationProjectStatus Status { get; set; }
    public DateTime? StartedTime { get; set; }
    public DateTime? CompletedTime { get; set; }
    public int FrameCount { get; set; }
    public int AcceptedFrameCount { get; set; }
    public int TargetCaptureCount { get; set; }
}

/// <summary>标定工程详情 DTO（含完整配置 + 帧子集合）</summary>
public class CalibrationProjectDetailDto : CalibrationProjectListDto
{
    public string? FailureReason { get; set; }

    // ── 标定板 ─────────────────────────────────────────────
    public CalibrationBoardType BoardType { get; set; }
    public int BoardRows { get; set; }
    public int BoardCols { get; set; }
    public double? SquareSizeMm { get; set; }
    public double? CircleDiameterMm { get; set; }
    public double? CircleSpacingMm { get; set; }
    public string? AprilTagFamily { get; set; }
    public double? AprilTagSizeMm { get; set; }
    public double? AprilTagSpacingMm { get; set; }
    public double BoardManufactureAccuracyMm { get; set; }

    // ── 采集配置 ───────────────────────────────────────────
    public double UnifiedExposureUs { get; set; }
    public double UnifiedGainDb { get; set; }
    public string? UnifiedWhiteBalance { get; set; }
    public ImageCaptureFormat ImageFormat { get; set; }
    public int? StructuredLightBrightness { get; set; }
    public int? PatternIntervalMs { get; set; }
    public int? CapturesPerPhase { get; set; }

    /// <summary>采集帧</summary>
    public List<CalibrationCaptureFrameDto> Frames { get; set; } = new();
}

/// <summary>采集帧 DTO</summary>
public class CalibrationCaptureFrameDto : EntityDto<Guid>
{
    public Guid CalibrationProjectId { get; set; }
    public int FrameIndex { get; set; }
    public DateTime CapturedTime { get; set; }
    public bool IsAccepted { get; set; }
    public string? RejectionReason { get; set; }
    public List<CalibrationCaptureImageDto> Images { get; set; } = new();
}

/// <summary>采集图像 DTO（不含字节流，仅元数据）</summary>
public class CalibrationCaptureImageDto : EntityDto<Guid>
{
    public Guid CalibrationCaptureFrameId { get; set; }
    public Guid CameraDeviceId { get; set; }
    public CameraRole CameraRole { get; set; }
    public string BlobName { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public long FileSizeBytes { get; set; }
    public double? ReprojectionError { get; set; }
}

// ═══════════════════════════════════════════════════════════════════════════
// 工程输入 DTO
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>查询标定工程列表</summary>
public class GetCalibrationProjectListInput : PagedAndSortedResultRequestDto
{
    public string? Filter { get; set; }
    public Guid? CalibrationDeviceId { get; set; }
    public CalibrationProjectStatus? Status { get; set; }
}

/// <summary>创建标定工程</summary>
public class CreateCalibrationProjectDto
{
    [Required]
    [StringLength(CalibrationManagementConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public Guid CalibrationDeviceId { get; set; }

    [StringLength(CalibrationManagementConsts.MaxDescriptionLength)]
    public string? Description { get; set; }
}

/// <summary>更新标定工程基础信息</summary>
public class UpdateCalibrationProjectDto
{
    [Required]
    [StringLength(CalibrationManagementConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(CalibrationManagementConsts.MaxDescriptionLength)]
    public string? Description { get; set; }
}

/// <summary>切换工程状态</summary>
public class TransitionProjectStatusInput
{
    [Required]
    public CalibrationProjectStatus Status { get; set; }

    [StringLength(CalibrationManagementConsts.MaxDescriptionLength)]
    public string? FailureReason { get; set; }
}

/// <summary>设置标定板配置</summary>
public class SetBoardConfigInput
{
    [Required]
    public CalibrationBoardType BoardType { get; set; }

    [Required]
    public int Rows { get; set; }

    [Required]
    public int Cols { get; set; }

    [Required]
    public double ManufactureAccuracyMm { get; set; }

    public double? SquareSizeMm { get; set; }
    public double? CircleDiameterMm { get; set; }
    public double? CircleSpacingMm { get; set; }
    public string? AprilTagFamily { get; set; }
    public double? AprilTagSizeMm { get; set; }
    public double? AprilTagSpacingMm { get; set; }
}

/// <summary>设置统一采集参数</summary>
public class SetCaptureConfigInput
{
    [Required]
    [Range(1, 500)]
    public int TargetCaptureCount { get; set; }

    [Required]
    public double UnifiedExposureUs { get; set; }

    [Required]
    public double UnifiedGainDb { get; set; }

    [Required]
    public ImageCaptureFormat ImageFormat { get; set; }

    public string? UnifiedWhiteBalance { get; set; }
    public int? StructuredLightBrightness { get; set; }
    public int? PatternIntervalMs { get; set; }
    public int? CapturesPerPhase { get; set; }
}

/// <summary>拒绝采集帧</summary>
public class RejectFrameInput
{
    [Required]
    [StringLength(CalibrationManagementConsts.MaxDescriptionLength)]
    public string Reason { get; set; } = string.Empty;
}
