using AuroraStruct3D.Projects;
using Volo.Abp.Application.Dtos;

namespace AuroraStruct3D.Projects.Dtos;

/// <summary>
/// 项目信息输出 DTO
/// </summary>
public class ProjectInfoDto : FullAuditedEntityDto<Guid>
{
    /// <summary>项目编号</summary>
    public string ProjectCode { get; set; } = string.Empty;

    /// <summary>项目名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>版本号</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>项目描述</summary>
    public string? Description { get; set; }

    /// <summary>项目状态</summary>
    public ProjectStatus Status { get; set; }

    /// <summary>项目状态中文描述</summary>
    public string StatusDisplay { get; set; } = string.Empty;

    /// <summary>创建人用户名</summary>
    public string? CreatorUserName { get; set; }
}
