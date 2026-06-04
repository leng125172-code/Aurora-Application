using System.ComponentModel.DataAnnotations;
using AuroraStruct3D.Calibration;

namespace AuroraStruct3D.Calibration.Dtos;

/// <summary>
/// 新增标定项目的输入 DTO
/// </summary>
public class CreateCalibProjectInput
{
    /// <summary>项目名称（必填，最长 256 字符）</summary>
    [Required]
    [MaxLength(CalibConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    /// <summary>项目描述（可选，最长 1024 字符）</summary>
    [MaxLength(CalibConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>设备类型（必填）</summary>
    [Required]
    public CalibDeviceType DeviceType { get; set; }
}

/// <summary>
/// 修改标定项目的输入 DTO（仅允许修改名称和描述）
/// </summary>
public class UpdateCalibProjectInput
{
    /// <summary>项目名称（必填，最长 256 字符）</summary>
    [Required]
    [MaxLength(CalibConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    /// <summary>项目描述（可选，最长 1024 字符）</summary>
    [MaxLength(CalibConsts.MaxDescriptionLength)]
    public string? Description { get; set; }
}
