using System.ComponentModel.DataAnnotations;
using AuroraStruct3D.Projects;

namespace AuroraStruct3D.Projects.Dtos;

/// <summary>
/// 创建项目输入 DTO
/// </summary>
public class CreateProjectInfoInput
{
    /// <summary>项目编号（唯一，如 PRJ-2024-001）</summary>
    [Required]
    [MaxLength(ProjectInfoConsts.MaxProjectCodeLength)]
    public string ProjectCode { get; set; } = string.Empty;

    /// <summary>项目名称</summary>
    [Required]
    [MaxLength(ProjectInfoConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    /// <summary>版本号（如 1.0.0）</summary>
    [Required]
    [MaxLength(ProjectInfoConsts.MaxVersionLength)]
    public string Version { get; set; } = string.Empty;

    /// <summary>项目描述</summary>
    [MaxLength(ProjectInfoConsts.MaxDescriptionLength)]
    public string? Description { get; set; }
}
