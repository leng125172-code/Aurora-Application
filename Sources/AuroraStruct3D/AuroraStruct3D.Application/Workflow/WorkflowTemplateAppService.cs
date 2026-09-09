using System.Text.Json;
using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;
using AuroraStruct3D.Workflow.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.Workflow;

/// <summary>工作流模板库应用服务。</summary>
[Authorize]
[Route("api/app/workflow-template")]
public class WorkflowTemplateAppService : AuroraStruct3DAppService, IWorkflowTemplateAppService
{
    private readonly IRepository<WorkflowTemplate, Guid> _templateRepository;
    private readonly IRepository<WorkflowDefinition, Guid> _workflowRepository;
    private readonly IWorkflowAppService _workflowAppService;

    public WorkflowTemplateAppService(
        IRepository<WorkflowTemplate, Guid> templateRepository,
        IRepository<WorkflowDefinition, Guid> workflowRepository,
        IWorkflowAppService workflowAppService
    )
    {
        _templateRepository = templateRepository;
        _workflowRepository = workflowRepository;
        _workflowAppService = workflowAppService;
    }

    [HttpGet]
    public async Task<List<WorkflowTemplateListItemDto>> GetListAsync(
        string? filter = null,
        string? category = null
    )
    {
        IQueryable<WorkflowTemplate> query = await _templateRepository.GetQueryableAsync();
        string keyword = filter?.Trim() ?? string.Empty;
        string categoryValue = category?.Trim() ?? string.Empty;

        if (keyword.Length > 0)
        {
            query = query.Where(x =>
                x.Name.Contains(keyword)
                || (x.Description != null && x.Description.Contains(keyword))
                || x.Category.Contains(keyword)
            );
        }
        if (categoryValue.Length > 0)
        {
            query = query.Where(x => x.Category == categoryValue);
        }

        List<WorkflowTemplate> templates = await AsyncExecuter.ToListAsync(
            query.OrderByDescending(x => x.LastModificationTime ?? x.CreationTime)
        );
        return templates.Select(MapToListItemDto).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<WorkflowTemplateDto> GetAsync(Guid id) =>
        MapToDto(await _templateRepository.GetAsync(id));

    [HttpPost]
    public async Task<WorkflowTemplateDto> CreateAsync(CreateWorkflowTemplateInput input)
    {
        WorkflowDefinition workflow = await GetSourceWorkflowAsync(input.WorkflowId);
        WorkflowTemplate template = new(
            GuidGenerator.Create(),
            input.Name,
            input.Category,
            input.Description,
            workflow.GraphData,
            workflow.Id
        );
        await _templateRepository.InsertAsync(template, autoSave: true);
        return MapToDto(template);
    }

    [HttpPut("{id:guid}")]
    public async Task<WorkflowTemplateDto> UpdateAsync(
        Guid id,
        UpdateWorkflowTemplateInput input
    )
    {
        WorkflowDefinition workflow = await GetSourceWorkflowAsync(input.WorkflowId);
        WorkflowTemplate template = await _templateRepository.GetAsync(id);
        template.Update(
            input.Name,
            input.Category,
            input.Description,
            workflow.GraphData,
            workflow.Id
        );
        await _templateRepository.UpdateAsync(template, autoSave: true);
        return MapToDto(template);
    }

    [HttpPost("{id:guid}/instantiate")]
    public async Task<WorkflowDto> InstantiateAsync(
        Guid id,
        InstantiateWorkflowTemplateInput input
    )
    {
        if (input.ProjectId == Guid.Empty)
        {
            throw new UserFriendlyException("请选择模板要添加到的项目。");
        }
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            throw new UserFriendlyException("请输入新工作流名称。");
        }

        WorkflowTemplate template = await _templateRepository.GetAsync(id);
        WorkflowGraphDto graph = JsonSerializer.Deserialize<WorkflowGraphDto>(
            template.GraphData,
            GraphJson.Options
        ) ?? throw new UserFriendlyException("模板中的工作流图数据无效。");

        WorkflowDto workflow = await _workflowAppService.CreateAsync(
            new CreateWorkflowInput
            {
                ProjectId = input.ProjectId,
                Name = input.Name.Trim(),
                GraphData = graph,
            }
        );

        template.RecordUsage();
        await _templateRepository.UpdateAsync(template, autoSave: true);
        return workflow;
    }

    [HttpDelete("{id:guid}")]
    public async Task DeleteAsync(Guid id) =>
        await _templateRepository.DeleteAsync(id, autoSave: true);

    private async Task<WorkflowDefinition> GetSourceWorkflowAsync(Guid workflowId)
    {
        if (workflowId == Guid.Empty)
        {
            throw new UserFriendlyException("请选择要保存为模板的工作流。");
        }
        return await _workflowRepository.GetAsync(workflowId);
    }

    private static WorkflowTemplateDto MapToDto(WorkflowTemplate template)
    {
        using JsonDocument graph = JsonDocument.Parse(template.GraphData);
        return new WorkflowTemplateDto
        {
            Id = template.Id,
            Name = template.Name,
            Category = template.Category,
            Description = template.Description,
            SourceWorkflowId = template.SourceWorkflowId,
            UsageCount = template.UsageCount,
            GraphData = graph.RootElement.Clone(),
            CreationTime = template.CreationTime,
            CreatorId = template.CreatorId,
            LastModificationTime = template.LastModificationTime,
            LastModifierId = template.LastModifierId,
            IsDeleted = template.IsDeleted,
            DeletionTime = template.DeletionTime,
            DeleterId = template.DeleterId,
        };
    }

    private static WorkflowTemplateListItemDto MapToListItemDto(WorkflowTemplate template)
    {
        using JsonDocument graph = JsonDocument.Parse(template.GraphData);
        return new WorkflowTemplateListItemDto
        {
            Id = template.Id,
            Name = template.Name,
            GraphData = graph.RootElement.Clone(),
        };
    }
}
