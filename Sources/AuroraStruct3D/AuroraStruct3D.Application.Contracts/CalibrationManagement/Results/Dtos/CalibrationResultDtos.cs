using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace AuroraStruct3D.CalibrationManagement.Results.Dtos;

// ═══════════════════════════════════════════════════════════════════════════
// 输出 DTO
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>标定结果列表项 DTO</summary>
public class CalibrationResultListDto : FullAuditedEntityDto<Guid>
{
    public Guid CalibrationProjectId { get; set; }
    public Guid CalibrationDeviceId { get; set; }
    public int Version { get; set; }
    public bool IsActive { get; set; }
    public DateTime ComputedTime { get; set; }
    public double OverallReprojectionError { get; set; }
    public double RmsError { get; set; }
    public int ValidationCount { get; set; }
}

/// <summary>标定结果详情 DTO</summary>
public class CalibrationResultDetailDto : CalibrationResultListDto
{
    public string CameraIntrinsicsJson { get; set; } = "[]";
    public string CameraExtrinsicsJson { get; set; } = "[]";
    public string? StructuredLightCalibrationJson { get; set; }

    public double MaxError { get; set; }
    public double MinError { get; set; }
    public double MeanError { get; set; }

    public List<CalibrationValidationRecordDto> Validations { get; set; } = new();
}

/// <summary>验证记录 DTO</summary>
public class CalibrationValidationRecordDto : EntityDto<Guid>
{
    public Guid CalibrationResultId { get; set; }
    public CalibrationValidationType ValidationType { get; set; }
    public bool IsPassed { get; set; }
    public string? MetricsJson { get; set; }
    public string? ReportBlobName { get; set; }
    public string? Remarks { get; set; }
    public DateTime ValidatedTime { get; set; }
}

// ═══════════════════════════════════════════════════════════════════════════
// 输入 DTO
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>查询标定结果列表</summary>
public class GetCalibrationResultListInput : PagedAndSortedResultRequestDto
{
    public Guid? CalibrationProjectId { get; set; }
    public Guid? CalibrationDeviceId { get; set; }
    public bool? IsActive { get; set; }
}

/// <summary>新增验证记录</summary>
public class AddValidationRecordInput
{
    [Required]
    public CalibrationValidationType ValidationType { get; set; }

    [Required]
    public bool IsPassed { get; set; }

    public string? MetricsJson { get; set; }
    public string? ReportBlobName { get; set; }

    [StringLength(CalibrationManagementConsts.MaxDescriptionLength)]
    public string? Remarks { get; set; }
}
