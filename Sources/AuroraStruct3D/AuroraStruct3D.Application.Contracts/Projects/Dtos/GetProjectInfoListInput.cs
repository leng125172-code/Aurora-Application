using AuroraStruct3D.Projects;
using Volo.Abp.Application.Dtos;

namespace AuroraStruct3D.Projects.Dtos;

/// <summary>
/// 项目列表分页查询输入
/// </summary>
public class GetProjectInfoListInput : PagedAndSortedResultRequestDto
{
    /// <summary>关键字（匹配项目编号或项目名称）</summary>
    public string? Filter { get; set; }

    /// <summary>项目状态过滤（null 表示查询全部）</summary>
    public ProjectStatus? Status { get; set; }
}
