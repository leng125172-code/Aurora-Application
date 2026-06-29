using Volo.Abp.Application.Dtos;

namespace AuroraStruct3D.Workflow.Dtos;

/// <summary>
/// 工作流列表项 DTO（轻量，不含 Content 全文）。
/// 用于按项目查询工作流列表时返回，避免传输完整 JSON 内容。
/// </summary>
public class WorkflowBriefDto : FullAuditedEntityDto<Guid>
{
    /// <summary>所属项目唯一标识。</summary>
    public Guid ProjectId { get; set; }

    /// <summary>工作流名称。</summary>
    public string Name { get; set; } = string.Empty;
}