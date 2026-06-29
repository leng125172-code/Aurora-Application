using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Workflow;

/// <summary>
/// 工作流定义聚合根。
/// 由前端工作流编辑器离线编辑完成后整体提交，<see cref="Content"/> 保存完整的
/// WorkflowPayload JSON（含 graphData、容器嵌套、ROI/标注等），后端按原文存储不做解析。
/// 审计信息（创建人/时间、修改人/时间，即 updatedAt）由 FullAuditedAggregateRoot 自动记录。
/// <para>数据库表：AbpProWorkflowDefinitions</para>
/// </summary>
public class WorkflowDefinition : FullAuditedAggregateRoot<Guid>
{
    /// <summary>所属项目唯一标识。</summary>
    public Guid ProjectId { get; private set; }

    /// <summary>工作流名称。</summary>
    public string Name { get; private set; } = null!;

    /// <summary>完整的 WorkflowPayload JSON 文本（按原文存储）。</summary>
    public string Content { get; private set; } = null!;

    /// <summary>EF Core 所需的无参构造函数（不得直接使用）。</summary>
    protected WorkflowDefinition() { }

    /// <summary>
    /// 工厂方法：新建一个工作流定义。
    /// </summary>
    /// <param name="id">工作流唯一标识</param>
    /// <param name="projectId">所属项目 ID</param>
    /// <param name="name">工作流名称</param>
    /// <param name="content">完整 WorkflowPayload JSON 文本</param>
    public static WorkflowDefinition Create(Guid id, Guid projectId, string name, string content)
    {
        Check.NotNullOrWhiteSpace(name, nameof(name), WorkflowDefinitionConsts.MaxNameLength);
        Check.NotNullOrWhiteSpace(content, nameof(content));

        return new WorkflowDefinition
        {
            Id = id,
            ProjectId = projectId,
            Name = name,
            Content = content,
        };
    }

    /// <summary>
    /// 修改保存：更新工作流名称与内容。
    /// </summary>
    /// <param name="name">工作流名称</param>
    /// <param name="content">完整 WorkflowPayload JSON 文本</param>
    public void Update(string name, string content)
    {
        Check.NotNullOrWhiteSpace(name, nameof(name), WorkflowDefinitionConsts.MaxNameLength);
        Check.NotNullOrWhiteSpace(content, nameof(content));

        Name = name;
        Content = content;
    }
}
