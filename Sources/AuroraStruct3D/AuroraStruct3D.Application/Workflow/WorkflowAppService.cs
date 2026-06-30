using System.Text.Json;
using AuroraStruct3D.OpenCV.Registry;
using AuroraStruct3D.OpenCV.Workflow.Compilation;
using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;
using AuroraStruct3D.Workflow.Dtos;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.Workflow;

/// <summary>
/// 工作流应用服务实现。
/// 新建 / 修改保存直接接收完整 WorkflowPayload（顶层含 projectId / name / graphData），
/// 后端从中读取 projectId、name、graphData 落库
/// 不再要求外层 { projectId, name, content } 包装。审计信息由 ABP 自动记录。
/// ABP 自动生成 REST 端点，基础路径：<c>/api/app/workflow</c>。
/// </summary>
public class WorkflowAppService : AuroraStruct3DAppService, IWorkflowAppService
{
    private readonly IRepository<WorkflowDefinition, Guid> _repository;
    private readonly IOperatorRegistry _operatorRegistry;

    public WorkflowAppService(
        IRepository<WorkflowDefinition, Guid> repository,
        IOperatorRegistry operatorRegistry
    )
    {
        _repository = repository;
        _operatorRegistry = operatorRegistry;
    }

    /// <inheritdoc/>
    public async Task<List<WorkflowBriefDto>> GetListByProjectIdAsync(Guid projectId)
    {
        IQueryable<WorkflowDefinition> queryable = await _repository.GetQueryableAsync();

        return await AsyncExecuter.ToListAsync(
            queryable
                .Where(w => w.ProjectId == projectId)
                .OrderByDescending(w => w.LastModificationTime)
                .Select(w => new WorkflowBriefDto
                {
                    Id = w.Id,
                    ProjectId = w.ProjectId,
                    Name = w.Name,
                    CreationTime = w.CreationTime,
                    CreatorId = w.CreatorId,
                    LastModificationTime = w.LastModificationTime,
                    LastModifierId = w.LastModifierId,
                })
        );
    }

    /// <inheritdoc/>
    public async Task<WorkflowDto> CreateAsync([FromBody] JsonElement payload)
    {
        (Guid projectId, string name, string graphData) = ReadPayload(payload);

        WorkflowDefinition workflow = WorkflowDefinition.Create(
            GuidGenerator.Create(),
            projectId,
            name,
            graphData
        );

        await _repository.InsertAsync(workflow, autoSave: true);

        return MapToDto(workflow);
    }

    /// <inheritdoc/>
    public async Task<WorkflowDto> GetAsync(Guid projectId, Guid id)
    {
        WorkflowDefinition workflow = await _repository.GetAsync(id);
        EnsureBelongsToProject(workflow, projectId);

        return MapToDto(workflow);
    }

    /// <inheritdoc/>
    public async Task<WorkflowDto> UpdateAsync(Guid id, [FromBody] JsonElement payload)
    {
        (Guid projectId, string name, string graphData) = ReadPayload(payload);

        WorkflowDefinition workflow = await _repository.GetAsync(id);
        EnsureBelongsToProject(workflow, projectId);

        workflow.Update(name, graphData);

        await _repository.UpdateAsync(workflow, autoSave: true);

        return MapToDto(workflow);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(Guid projectId, Guid id)
    {
        WorkflowDefinition workflow = await _repository.GetAsync(id);
        EnsureBelongsToProject(workflow, projectId);

        await _repository.DeleteAsync(workflow, autoSave: true);
    }

    /// <inheritdoc/>
    public async Task<WorkflowValidateResultDto> ValidateAsync(Guid projectId, Guid id)
    {
        WorkflowDefinition workflow = await _repository.GetAsync(id);
        EnsureBelongsToProject(workflow, projectId);

        // 解析 graphData JSON 为图模型。
        (_, GraphDataModel graph) = WorkflowGraphCompiler.ParseContent(workflow.GraphData);

        // 执行静态校验。
        WorkflowValidationResult validation = await new WorkflowGraphValidator(_operatorRegistry)
            .ValidateAsync(graph);

        return new WorkflowValidateResultDto
        {
            HasErrors = validation.HasErrors,
            Errors = validation.Errors.Select(e => new WorkflowDiagnosticDto
            {
                Severity = e.Severity.ToString(),
                NodeId = e.NodeId,
                Target = e.Target,
                Message = e.Message,
            }).ToList(),
            Warnings = validation.Warnings.Select(w => new WorkflowDiagnosticDto
            {
                Severity = w.Severity.ToString(),
                NodeId = w.NodeId,
                Target = w.Target,
                Message = w.Message,
            }).ToList(),
        };
    }

    /// <inheritdoc/>
    public async Task<WorkflowDataFlowReportDto> SimulateAsync(Guid projectId, Guid id)
    {
        WorkflowDefinition workflow = await _repository.GetAsync(id);
        EnsureBelongsToProject(workflow, projectId);

        // 解析并执行数据流仿真。
        (string name, GraphDataModel graph) = WorkflowGraphCompiler.ParseContent(workflow.GraphData);

        WorkflowDataFlowReport report = await new WorkflowDataFlowSimulator(_operatorRegistry)
            .SimulateAsync(graph, name);

        return new WorkflowDataFlowReportDto
        {
            WorkflowName = report.WorkflowName,
            TotalNodeCount = report.TotalNodeCount,
            OperatorNodeCount = report.OperatorNodeCount,
            TotalVariableCount = report.TotalVariableCount,
            TopologyDepth = report.TopologyDepth,
            Nodes = report.Nodes.Select(n => new NodeDataFlowInfoDto
            {
                NodeId = n.NodeId,
                NodeType = n.NodeType,
                DisplayName = n.DisplayName,
                TopologyLayer = n.TopologyLayer,
                Consumes = n.Consumes.Select(c => new VariablePortRefDto
                {
                    VariableName = c.VariableName,
                    PortName = c.PortName,
                    TypeName = c.TypeName,
                }).ToList(),
                Produces = n.Produces.Select(p => new VariablePortRefDto
                {
                    VariableName = p.VariableName,
                    PortName = p.PortName,
                    TypeName = p.TypeName,
                }).ToList(),
            }).ToList(),
            Variables = report.Variables.Select(v => new VariableLifecycleDto
            {
                VariableName = v.VariableName,
                TypeName = v.TypeName,
                DefinedByNodeId = v.DefinedByNodeId,
                ConsumedByNodeIds = v.ConsumedByNodeIds.ToList(),
            }).ToList(),
        };
    }

    // ─────────────────────────── 私有辅助 ───────────────────────────

    /// <summary>
    /// 从 WorkflowPayload 中解析 projectId / name，并提取 graphData 作为入库内容。
    /// </summary>
    private static (Guid ProjectId, string Name, string GraphData) ReadPayload(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new UserFriendlyException("请求体必须是工作流 JSON 对象。");
        }

        if (
            !payload.TryGetProperty("projectId", out JsonElement projectIdElement)
            || projectIdElement.ValueKind != JsonValueKind.String
            || !Guid.TryParse(projectIdElement.GetString(), out Guid projectId)
            || projectId == Guid.Empty
        )
        {
            throw new UserFriendlyException("工作流缺少有效的 projectId。");
        }

        string name =
            payload.TryGetProperty("name", out JsonElement nameElement)
            && nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString() ?? string.Empty
                : string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new UserFriendlyException("工作流缺少名称 name。");
        }

        if (!payload.TryGetProperty("graphData", out JsonElement graphDataElement))
        {
            throw new UserFriendlyException("工作流内容缺少 graphData。");
        }

        return (projectId, name, graphDataElement.GetRawText());
    }

    /// <summary>
    /// 校验工作流归属：防止跨项目读取 / 修改他人工作流。
    /// </summary>
    private static void EnsureBelongsToProject(WorkflowDefinition workflow, Guid projectId)
    {
        if (workflow.ProjectId != projectId)
        {
            throw new UserFriendlyException("工作流不存在或不属于该项目。");
        }
    }

    /// <summary>
    /// 将实体映射为输出 DTO。
    /// <see cref="WorkflowDefinition.GraphData"/> 直接解析为 JsonElement 返回。
    /// </summary>
    private static WorkflowDto MapToDto(WorkflowDefinition workflow)
    {
        using JsonDocument doc = JsonDocument.Parse(workflow.GraphData);

        return new WorkflowDto
        {
            Id = workflow.Id,
            ProjectId = workflow.ProjectId,
            Name = workflow.Name,
            GraphData = doc.RootElement.Clone(),
            CreationTime = workflow.CreationTime,
            CreatorId = workflow.CreatorId,
            LastModificationTime = workflow.LastModificationTime,
            LastModifierId = workflow.LastModifierId,
            IsDeleted = workflow.IsDeleted,
            DeletionTime = workflow.DeletionTime,
            DeleterId = workflow.DeleterId,
        };
    }
}
