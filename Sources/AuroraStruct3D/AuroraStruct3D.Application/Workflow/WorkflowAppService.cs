using System.Text.Json;
using AuroraStruct3D.OpenCV.Registry;
using AuroraStruct3D.OpenCV.Workflow.Compilation;
using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;
using AuroraStruct3D.Variables;
using AuroraStruct3D.Variables.Dtos;
using AuroraStruct3D.Workflow.Dtos;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.Workflow;

/// <summary>
/// 工作流应用服务实现。
/// 新建 / 修改保存接收强类型请求 DTO（<see cref="CreateWorkflowInput"/> / <see cref="UpdateWorkflowInput"/>），
/// 并将 graphData 严格校验后由后端自动组装变量编译输入，再执行离线变量编译并落库。
/// 审计信息由 ABP 自动记录。
/// 端点使用 ABP 约定式路由。
/// </summary>
[Authorize]
public class WorkflowAppService : AuroraStruct3DAppService, IWorkflowAppService
{
    private readonly IRepository<WorkflowDefinition, Guid> _repository;
    private readonly IOperatorRegistry _operatorRegistry;
    private readonly IOfflineVariableLibraryAppService _offlineVariableLibrary;

    public WorkflowAppService(
        IRepository<WorkflowDefinition, Guid> repository,
        IOperatorRegistry operatorRegistry,
        IOfflineVariableLibraryAppService offlineVariableLibrary
    )
    {
        _repository = repository;
        _operatorRegistry = operatorRegistry;
        _offlineVariableLibrary = offlineVariableLibrary;
    }

    /// <inheritdoc/>
    public async Task<List<WorkflowBriefDto>> GetListAsync(Guid projectId)
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
    public async Task<WorkflowDto> CreateAsync(CreateWorkflowInput input)
    {
        (Guid projectId, string name, string graphData, GraphDataModel graph) = ReadPayload(input);

        Guid workflowId = GuidGenerator.Create();

        // MOD: 保存前执行图校验 + 离线变量编译，闭合运行时/编译时双重校验。
        await ValidateBeforeSaveAsync(projectId, workflowId, graph);

        WorkflowDefinition workflow = WorkflowDefinition.Create(
            workflowId,
            projectId,
            name,
            graphData
        );

        await _repository.InsertAsync(workflow, autoSave: true);

        return MapToDto(workflow);
    }

    /// <inheritdoc/>
    public async Task<WorkflowDto> GetAsync(Guid id)
    {
        WorkflowDefinition workflow = await _repository.GetAsync(id);

        return MapToDto(workflow);
    }

    /// <inheritdoc/>
    public async Task<WorkflowDto> UpdateAsync(Guid id, UpdateWorkflowInput input)
    {
        (Guid projectId, string name, string graphData, GraphDataModel graph) = ReadPayload(input);

        WorkflowDefinition workflow = await _repository.GetAsync(id);
        EnsureBelongsToProject(workflow, projectId);

        // MOD: 更新同样要求先通过离线变量编译。
        await ValidateBeforeSaveAsync(projectId, workflow.Id, graph);

        workflow.Update(name, graphData);

        await _repository.UpdateAsync(workflow, autoSave: true);

        return MapToDto(workflow);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(Guid id)
    {
        WorkflowDefinition workflow = await _repository.GetAsync(id);

        await _repository.DeleteAsync(workflow, autoSave: true);
    }

    /// <inheritdoc/>
    public async Task<WorkflowValidateResultDto> ValidateAsync(Guid id)
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
    public async Task<WorkflowDataFlowReportDto> SimulateAsync(Guid id)
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
    private static (
        Guid ProjectId,
        string Name,
        string GraphData,
        GraphDataModel Graph
    ) ReadPayload(CreateWorkflowInput input)
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

        (string validatedGraphData, GraphDataModel graph) = ValidateAndSerializeGraphData(
            input.GraphData
        );
        return (input.ProjectId, input.Name, validatedGraphData, graph);
    }

    private static (
        Guid ProjectId,
        string Name,
        string GraphData,
        GraphDataModel Graph
    ) ReadPayload(UpdateWorkflowInput input)
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

        (string validatedGraphData, GraphDataModel graph) = ValidateAndSerializeGraphData(
            input.GraphData
        );
        return (input.ProjectId, input.Name, validatedGraphData, graph);
    }

    private static (string GraphJson, GraphDataModel Graph) ValidateAndSerializeGraphData(
        WorkflowGraphDto graphData
    )
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

        return (JsonSerializer.Serialize(graph, GraphJson.Options), graph);
    }

    private async Task ValidateBeforeSaveAsync(
        Guid projectId,
        Guid workflowId,
        GraphDataModel graph
    )
    {
        VariableDefUseAnalysisMode defUseAnalysisMode = VariableDefUseAnalysisMode.Conservative;

        // MOD: 严格模式下启用重复定义错误（支持显式 shadow）。
        bool strictValidation = defUseAnalysisMode == VariableDefUseAnalysisMode.Strict;
        WorkflowValidationResult graphValidation = await new WorkflowGraphValidator(
            _operatorRegistry,
            new WorkflowValidationOptions { StrictVariableDefinition = strictValidation }
        ).ValidateAsync(graph);

        if (graphValidation.HasErrors)
        {
            throw new UserFriendlyException(
                "工作流图校验失败："
                    + string.Join(" | ", graphValidation.Errors.Select(x => x.Message))
            );
        }

        VariableCompileRequestDto variableCompileRequest = await BuildVariableCompileRequestAsync(
            projectId,
            workflowId,
            graph,
            defUseAnalysisMode
        );

        VariableCompileResultDto compileResult = await _offlineVariableLibrary.CompileAsync(
            variableCompileRequest
        );

        if (!compileResult.CanPublish)
        {
            List<string> errors = compileResult
                .Diagnostics.Where(x => x.Severity == VariableDiagnosticSeverity.Error)
                .Select(x => x.Message)
                .Take(10)
                .ToList();
            throw new UserFriendlyException("离线变量编译未通过：" + string.Join(" | ", errors));
        }
    }

    private async Task<VariableCompileRequestDto> BuildVariableCompileRequestAsync(
        Guid projectId,
        Guid workflowId,
        GraphDataModel graph,
        VariableDefUseAnalysisMode defUseAnalysisMode
    )
    {
        string? entryNodeId = graph
            .Nodes.FirstOrDefault(x =>
                string.Equals(x.Type, "start-node", StringComparison.Ordinal)
            )
            ?.Id;
        if (string.IsNullOrWhiteSpace(entryNodeId))
        {
            throw new UserFriendlyException(
                "variableCompileRequest.EntryNodeId 不能为空。请确保图中存在 start-node。"
            );
        }

        List<VariableControlFlowEdgeDto> controlFlowEdges = graph
            .Edges.Where(x =>
                !string.IsNullOrWhiteSpace(x.SourceNodeId)
                && !string.IsNullOrWhiteSpace(x.TargetNodeId)
            )
            .Select(x => new VariableControlFlowEdgeDto
            {
                FromNodeId = x.SourceNodeId!,
                ToNodeId = x.TargetNodeId!,
            })
            .ToList();

        if (controlFlowEdges.Count == 0)
        {
            throw new UserFriendlyException(
                "variableCompileRequest.ControlFlowEdges 不能为空，必须提供完整 CFG。"
            );
        }

        List<Guid> operatorIds = graph
            .Nodes.Select(node => Guid.TryParse(node.Type, out Guid parsed) ? parsed : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        Dictionary<Guid, OperatorParametersDescriptor?> operatorParameterMap = new();
        foreach (Guid operatorId in operatorIds)
        {
            operatorParameterMap[operatorId] = await _operatorRegistry.GetParametersAsync(
                operatorId
            );
        }

        Dictionary<string, int> sequenceMap = graph
            .Nodes.Select((node, index) => new { node.Id, Sequence = index + 1 })
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .ToDictionary(x => x.Id, x => x.Sequence, StringComparer.Ordinal);

        List<VariableReferenceDto> reads = new();
        List<VariableReferenceDto> writes = new();
        Dictionary<string, VariableDeclarationDto> declarationMap = new(StringComparer.Ordinal);

        foreach (NodeModel node in graph.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id))
            {
                continue;
            }

            NodePropertiesModel properties = node.Properties ?? new NodePropertiesModel();
            Dictionary<string, string> inputBindings = properties.InputBindings ?? [];
            Dictionary<string, string> inputBindingSources = properties.InputBindingSources ?? [];
            Dictionary<string, JsonElement> @params = properties.Params ?? [];
            Dictionary<string, string> paramSources = properties.ParamSources ?? [];
            Dictionary<string, string> outputBindings = properties.OutputBindings ?? [];
            Dictionary<string, string> outputBindingSources = properties.OutputBindingSources ?? [];

            OperatorParametersDescriptor? parameters = null;
            if (Guid.TryParse(node.Type, out Guid operatorId))
            {
                parameters = operatorParameterMap.GetValueOrDefault(operatorId);
            }

            foreach ((string key, string variableNameRaw) in inputBindings)
            {
                if (
                    !string.Equals(
                        inputBindingSources.GetValueOrDefault(key),
                        "variable",
                        StringComparison.Ordinal
                    )
                )
                {
                    continue;
                }

                string variableName = variableNameRaw.Trim();
                if (string.IsNullOrWhiteSpace(variableName))
                {
                    throw new UserFriendlyException(
                        $"节点 {node.Id} 的输入绑定 {key} 变量名为空。"
                    );
                }

                string? expectedTypeName = parameters
                    ?.Inputs.FirstOrDefault(x =>
                        string.Equals(x.ParameterName, key, StringComparison.Ordinal)
                    )
                    ?.ParameterTypeName;

                reads.Add(
                    new VariableReferenceDto
                    {
                        OwnerWorkflowId = workflowId,
                        VariableName = variableName,
                        Location = $"{node.Id}:inputBindings:{key}",
                        Sequence = sequenceMap.GetValueOrDefault(node.Id),
                        ExpectedTypeName = expectedTypeName,
                    }
                );
            }

            foreach ((string key, JsonElement valueElement) in @params)
            {
                if (
                    !string.Equals(
                        paramSources.GetValueOrDefault(key),
                        "variable",
                        StringComparison.Ordinal
                    )
                )
                {
                    continue;
                }

                string variableName = ExtractVariableName(valueElement, node.Id, key);
                string? expectedTypeName = parameters
                    ?.Config.FirstOrDefault(x =>
                        string.Equals(x.Name, key, StringComparison.Ordinal)
                    )
                    ?.ParameterTypeName;

                reads.Add(
                    new VariableReferenceDto
                    {
                        OwnerWorkflowId = workflowId,
                        VariableName = variableName,
                        Location = $"{node.Id}:params:{key}",
                        Sequence = sequenceMap.GetValueOrDefault(node.Id),
                        ExpectedTypeName = expectedTypeName,
                    }
                );
            }

            foreach ((string key, string variableNameRaw) in outputBindings)
            {
                if (
                    !string.Equals(
                        outputBindingSources.GetValueOrDefault(key),
                        "variable",
                        StringComparison.Ordinal
                    )
                )
                {
                    continue;
                }

                string variableName = variableNameRaw.Trim();
                if (string.IsNullOrWhiteSpace(variableName))
                {
                    throw new UserFriendlyException(
                        $"节点 {node.Id} 的输出绑定 {key} 变量名为空。"
                    );
                }

                string? expectedTypeName = parameters
                    ?.Outputs.FirstOrDefault(x =>
                        string.Equals(x.ParameterName, key, StringComparison.Ordinal)
                    )
                    ?.ParameterTypeName;
                if (string.IsNullOrWhiteSpace(expectedTypeName))
                {
                    throw new UserFriendlyException(
                        $"节点 {node.Id} 的输出端口 {key} 缺少类型元数据，无法自动生成变量声明。"
                    );
                }

                writes.Add(
                    new VariableReferenceDto
                    {
                        OwnerWorkflowId = workflowId,
                        VariableName = variableName,
                        Location = $"{node.Id}:outputBindings:{key}",
                        Sequence = sequenceMap.GetValueOrDefault(node.Id),
                        ExpectedTypeName = expectedTypeName,
                    }
                );

                if (
                    declarationMap.TryGetValue(variableName, out VariableDeclarationDto? existing)
                    && !string.Equals(existing.TypeName, expectedTypeName, StringComparison.Ordinal)
                )
                {
                    throw new UserFriendlyException(
                        $"变量 {variableName} 在图中被赋予了冲突类型：{existing.TypeName} 与 {expectedTypeName}。"
                    );
                }

                declarationMap[variableName] = new VariableDeclarationDto
                {
                    Name = variableName,
                    TypeName = expectedTypeName,
                    Visibility = VariableVisibility.Private,
                    Mutability = VariableMutability.Mutable,
                    IsRequiredInit = false,
                    DefaultValueJson = null,
                };
            }
        }

        return new VariableCompileRequestDto
        {
            ProjectId = projectId,
            WorkflowId = workflowId,
            Declarations = declarationMap.Values.ToList(),
            Imports = [],
            Reads = reads,
            Writes = writes,
            EntryNodeId = entryNodeId,
            ControlFlowEdges = controlFlowEdges,
            DefUseAnalysisMode = defUseAnalysisMode,
            SuppressedDiagnosticCodes = [],
        };
    }

    private static string ExtractVariableName(JsonElement value, string nodeId, string paramName)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            string? text = value.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }

        if (
            value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty("$var", out JsonElement varElement)
            && varElement.ValueKind == JsonValueKind.String
        )
        {
            string? text = varElement.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }

        throw new UserFriendlyException(
            $"节点 {nodeId} 的参数 {paramName} 标记为 variable，但值不是合法变量引用。"
        );
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
