using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Workflow;

/// <summary>
/// 可复用的工作流模板快照。模板与项目工作流相互独立，实例化后修改工作流不会影响模板。
/// </summary>
public sealed class WorkflowTemplate : FullAuditedAggregateRoot<Guid>
{
    public string Name { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string GraphData { get; private set; } = "{}";
    public Guid? SourceWorkflowId { get; private set; }
    public int UsageCount { get; private set; }

    private WorkflowTemplate() { }

    public WorkflowTemplate(
        Guid id,
        string name,
        string category,
        string? description,
        string graphData,
        Guid? sourceWorkflowId = null
    ) : base(id)
    {
        Update(name, category, description, graphData, sourceWorkflowId);
    }

    public void Update(
        string name,
        string category,
        string? description,
        string graphData,
        Guid? sourceWorkflowId = null
    )
    {
        Name = Check.NotNullOrWhiteSpace(
            name,
            nameof(name),
            WorkflowTemplateConsts.MaxNameLength
        ).Trim();
        Category = Check.Length(
            category?.Trim() ?? string.Empty,
            nameof(category),
            WorkflowTemplateConsts.MaxCategoryLength
        )!;
        Description = string.IsNullOrWhiteSpace(description)
            ? null
            : Check.Length(
                description.Trim(),
                nameof(description),
                WorkflowTemplateConsts.MaxDescriptionLength
            );
        GraphData = Check.NotNullOrWhiteSpace(graphData, nameof(graphData));
        SourceWorkflowId = sourceWorkflowId;
    }

    public void RecordUsage() => UsageCount++;
}
