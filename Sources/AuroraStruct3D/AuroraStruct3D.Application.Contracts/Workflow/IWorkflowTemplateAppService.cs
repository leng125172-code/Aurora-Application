using AuroraStruct3D.Workflow.Dtos;
using Volo.Abp.Application.Services;

namespace AuroraStruct3D.Workflow;

/// <summary>工作流模板库。</summary>
public interface IWorkflowTemplateAppService : IApplicationService
{
    Task<List<WorkflowTemplateListItemDto>> GetListAsync(
        string? filter = null,
        string? category = null
    );
    Task<WorkflowTemplateDto> GetAsync(Guid id);
    Task<WorkflowTemplateDto> CreateAsync(CreateWorkflowTemplateInput input);
    Task<WorkflowTemplateDto> UpdateAsync(Guid id, UpdateWorkflowTemplateInput input);
    Task<WorkflowDto> InstantiateAsync(Guid id, InstantiateWorkflowTemplateInput input);
    Task DeleteAsync(Guid id);
}
