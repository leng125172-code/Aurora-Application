using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Volo.Abp.Application.Dtos;

namespace AuroraStruct3D.Workflow.Dtos;

/// <summary>
/// 流程库列表项。列表接口只返回模板标识、名称和可直接使用的流程图。
/// </summary>
public class WorkflowTemplateListItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public JsonElement GraphData { get; set; }
}

public class WorkflowTemplateBriefDto : FullAuditedEntityDto<Guid>
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? SourceWorkflowId { get; set; }
    public int UsageCount { get; set; }
}

public class WorkflowTemplateDto : WorkflowTemplateBriefDto
{
    public JsonElement GraphData { get; set; }
}

public class CreateWorkflowTemplateInput
{
    [Required]
    public Guid WorkflowId { get; set; }

    [Required]
    [MaxLength(WorkflowTemplateConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(WorkflowTemplateConsts.MaxCategoryLength)]
    public string Category { get; set; } = string.Empty;

    [MaxLength(WorkflowTemplateConsts.MaxDescriptionLength)]
    public string? Description { get; set; }
}

public class UpdateWorkflowTemplateInput : CreateWorkflowTemplateInput { }

public class InstantiateWorkflowTemplateInput
{
    [Required]
    public Guid ProjectId { get; set; }

    [Required]
    [MaxLength(WorkflowDefinitionConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;
}
