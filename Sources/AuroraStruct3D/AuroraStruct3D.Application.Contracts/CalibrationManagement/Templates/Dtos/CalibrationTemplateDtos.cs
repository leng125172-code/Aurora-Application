using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace AuroraStruct3D.CalibrationManagement.Templates.Dtos;

// ═══════════════════════════════════════════════════════════════════════════
// 相机模板
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>相机模板列表/详情 DTO</summary>
public class CalibrationCameraTemplateDto : FullAuditedEntityDto<Guid>
{
    public string Name { get; set; } = string.Empty;
    public string? CameraModel { get; set; }
    public string? Description { get; set; }
    public string ParametersJson { get; set; } = "{}";
}

/// <summary>查询相机模板列表的输入 DTO</summary>
public class GetCalibrationCameraTemplateListInput : PagedAndSortedResultRequestDto
{
    /// <summary>名称/型号模糊搜索</summary>
    public string? Filter { get; set; }
}

/// <summary>创建/更新相机模板</summary>
public class CreateUpdateCalibrationCameraTemplateDto
{
    [Required]
    [StringLength(CalibrationManagementConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(CalibrationManagementConsts.MaxNameLength)]
    public string? CameraModel { get; set; }

    [StringLength(CalibrationManagementConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>参数 JSON（必填，且需为有效 JSON 字符串）</summary>
    [Required]
    public string ParametersJson { get; set; } = "{}";
}

// ═══════════════════════════════════════════════════════════════════════════
// 投射器模板
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>投射器模板列表/详情 DTO</summary>
public class CalibrationProjectorTemplateDto : FullAuditedEntityDto<Guid>
{
    public string Name { get; set; } = string.Empty;
    public string? ProjectorModel { get; set; }
    public string? Description { get; set; }
    public string ParametersJson { get; set; } = "{}";
}

/// <summary>查询投射器模板列表的输入 DTO</summary>
public class GetCalibrationProjectorTemplateListInput : PagedAndSortedResultRequestDto
{
    /// <summary>名称/型号模糊搜索</summary>
    public string? Filter { get; set; }
}

/// <summary>创建/更新投射器模板</summary>
public class CreateUpdateCalibrationProjectorTemplateDto
{
    [Required]
    [StringLength(CalibrationManagementConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(CalibrationManagementConsts.MaxNameLength)]
    public string? ProjectorModel { get; set; }

    [StringLength(CalibrationManagementConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    [Required]
    public string ParametersJson { get; set; } = "{}";
}
