using System.ComponentModel.DataAnnotations;
using AuroraStruct3D.Projects;

namespace AuroraStruct3D.Projects.Dtos;

/// <summary>
/// 更新项目输入 DTO（不允许修改项目编号）
/// </summary>
public class UpdateProjectInfoInput
{
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
