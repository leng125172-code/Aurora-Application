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
/// 新建 / 修改保存接收强类型请求 DTO（<see cref="CreateWorkflowInput"/> / <see cref="UpdateWorkflowInput"/>），
/// 并将 graphData 严格校验后落库。审计信息由 ABP 自动记录。
/// 端点使用显式 HTTP 特性定义，避免约定式推断歧义。
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
    [HttpGet("projects/{projectId}/workflows")]
    public async Task<List<WorkflowBriefDto>> GetListByProjectIdAsync([FromRoute] Guid projectId)
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
    [HttpPost]
    public async Task<WorkflowDto> CreateAsync([FromBody] CreateWorkflowInput input)
    {
        (Guid projectId, string name, string graphData) = ReadPayload(input);

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
    [HttpGet("{id}")]
    public async Task<WorkflowDto> GetAsync([FromRoute] Guid id)
    {
        WorkflowDefinition workflow = await _repository.GetAsync(id);

        return MapToDto(workflow);
    }

    /// <inheritdoc/>
    [HttpPut("{id}")]
    public async Task<WorkflowDto> UpdateAsync(
        [FromRoute] Guid id,
        [FromBody] UpdateWorkflowInput input
    )
    {
        (Guid projectId, string name, string graphData) = ReadPayload(input);

        WorkflowDefinition workflow = await _repository.GetAsync(id);
        EnsureBelongsToProject(workflow, projectId);

        workflow.Update(name, graphData);

        await _repository.UpdateAsync(workflow, autoSave: true);

        return MapToDto(workflow);
    }

    /// <inheritdoc/>
    [HttpDelete("{id}")]
    public async Task DeleteAsync([FromRoute] Guid id)
    {
        WorkflowDefinition workflow = await _repository.GetAsync(id);

        await _repository.DeleteAsync(workflow, autoSave: true);
    }

    /// <inheritdoc/>
    [HttpPost("{id}/validate")]
    public async Task<WorkflowValidateResultDto> ValidateAsync([FromRoute] Guid id)
    {
        WorkflowDefinition workflow = await _repository.GetAsync(id);

        // 解析 graphData JSON 为图模型。
        (_, GraphDataModel graph) = WorkflowGraphCompiler.ParseContent(workflow.GraphData);

        // 执行静态校验。
        WorkflowValidationResult validation = await new WorkflowGraphValidator(
            _operatorRegistry
        ).ValidateAsync(graph);

        return new WorkflowValidateResultDto
        {
            HasErrors = validation.HasErrors,
            Errors = validation
                .Errors.Select(e => new WorkflowDiagnosticDto
                {
                    Severity = e.Severity.ToString(),
                    NodeId = e.NodeId,
                    Target = e.Target,
                    Message = e.Message,
                })
                .ToList(),
            Warnings = validation
                .Warnings.Select(w => new WorkflowDiagnosticDto
                {
                    Severity = w.Severity.ToString(),
                    NodeId = w.NodeId,
                    Target = w.Target,
                    Message = w.Message,
                })
                .ToList(),
        };
    }

    /// <inheritdoc/>
    [HttpPost("{id}/simulate")]
    public async Task<WorkflowDataFlowReportDto> SimulateAsync([FromRoute] Guid id)
    {
        WorkflowDefinition workflow = await _repository.GetAsync(id);

        // 解析并执行数据流仿真。
        (_, GraphDataModel graph) = WorkflowGraphCompiler.ParseContent(workflow.GraphData);

        WorkflowDataFlowReport report = await new WorkflowDataFlowSimulator(
            _operatorRegistry
        ).SimulateAsync(graph, workflow.Name);

        return new WorkflowDataFlowReportDto
        {
            WorkflowName = ResolveResponseWorkflowName(workflow, report.WorkflowName),
            TotalNodeCount = report.TotalNodeCount,
            OperatorNodeCount = report.OperatorNodeCount,
            TotalVariableCount = report.TotalVariableCount,
            TopologyDepth = report.TopologyDepth,
            Nodes = report
                .Nodes.Select(n => new NodeDataFlowInfoDto
                {
                    NodeId = n.NodeId,
                    NodeType = n.NodeType,
                    DisplayName = n.DisplayName,
                    TopologyLayer = n.TopologyLayer,
                    Consumes = n
                        .Consumes.Select(c => new VariablePortRefDto
                        {
                            VariableName = c.VariableName,
                            PortName = c.PortName,
                            TypeName = c.TypeName,
                        })
                        .ToList(),
                    Produces = n
                        .Produces.Select(p => new VariablePortRefDto
                        {
                            VariableName = p.VariableName,
                            PortName = p.PortName,
                            TypeName = p.TypeName,
                        })
                        .ToList(),
                })
                .ToList(),
            Variables = report
                .Variables.Select(v => new VariableLifecycleDto
                {
                    VariableName = v.VariableName,
                    TypeName = v.TypeName,
                    DefinedByNodeId = v.DefinedByNodeId,
                    ConsumedByNodeIds = v.ConsumedByNodeIds.ToList(),
                })
                .ToList(),
        };
    }

    // ─────────────────────────── 私有辅助 ───────────────────────────

    /// <summary>
    /// 从 WorkflowPayload 中解析 projectId / name，并提取 graphData 作为入库内容。
    /// </summary>
    private static (Guid ProjectId, string Name, string GraphData) ReadPayload(
        CreateWorkflowInput input
    )
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.ProjectId == Guid.Empty)
        {
            throw new UserFriendlyException("工作流缺少有效的 projectId。");
        }

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            throw new UserFriendlyException("工作流缺少名称 name。");
        }

        if (input.GraphData is null)
        {
            throw new UserFriendlyException("工作流内容缺少 graphData。");
        }

        string validatedGraphData = ValidateAndSerializeGraphData(input.GraphData);
        return (input.ProjectId, input.Name, validatedGraphData);
    }

    private static (Guid ProjectId, string Name, string GraphData) ReadPayload(
        UpdateWorkflowInput input
    )
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.ProjectId == Guid.Empty)
        {
            throw new UserFriendlyException("工作流缺少有效的 projectId。");
        }

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            throw new UserFriendlyException("工作流缺少名称 name。");
        }

        if (input.GraphData is null)
        {
            throw new UserFriendlyException("工作流内容缺少 graphData。");
        }

        string validatedGraphData = ValidateAndSerializeGraphData(input.GraphData);
        return (input.ProjectId, input.Name, validatedGraphData);
    }

    private static string ValidateAndSerializeGraphData(WorkflowGraphDto graphData)
    {
        string graphJsonText = JsonSerializer.Serialize(graphData, GraphJson.Options);
        GraphDataModel graph =
            JsonSerializer.Deserialize<GraphDataModel>(graphJsonText, GraphJson.Options)
            ?? new GraphDataModel();

        List<string> errors = WorkflowSourceFlagContractValidator.Validate(graph);
        if (errors.Count > 0)
        {
            throw new UserFriendlyException(
                "工作流 source flags 校验失败：" + string.Join(" | ", errors)
            );
        }

        return JsonSerializer.Serialize(graph, GraphJson.Options);
    }

    private static string ResolveResponseWorkflowName(
        WorkflowDefinition workflow,
        string fallbackName
    )
    {
        if (!string.IsNullOrWhiteSpace(workflow.Name))
        {
            return workflow.Name;
        }

        return fallbackName;
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
