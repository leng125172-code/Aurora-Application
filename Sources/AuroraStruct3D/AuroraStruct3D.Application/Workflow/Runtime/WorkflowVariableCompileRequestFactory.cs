using System.Text.Json;
using AuroraStruct3D.OpenCV.Registry;
using AuroraStruct3D.OpenCV.Workflow.Compilation;
using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;
using AuroraStruct3D.Variables;
using AuroraStruct3D.Variables.Dtos;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace AuroraStruct3D.Workflow.Runtime;

/// <summary>
/// 运行时执行前的变量编译请求构建器。
/// </summary>
public sealed class WorkflowVariableCompileRequestFactory : ITransientDependency
{
    private readonly IOperatorRegistry _operatorRegistry;

    /// <summary>
    /// 初始化构建器。
    /// </summary>
    /// <param name="operatorRegistry">算子注册表。</param>
    public WorkflowVariableCompileRequestFactory(IOperatorRegistry operatorRegistry)
    {
        _operatorRegistry = operatorRegistry;
    }

    /// <summary>
    /// 基于图模型构建离线变量编译请求。
    /// </summary>
    /// <param name="projectId">项目 ID。</param>
    /// <param name="workflowId">工作流 ID。</param>
    /// <param name="graph">图模型。</param>
    /// <param name="defUseMode">Def-Use 分析模式。</param>
    /// <returns>编译请求。</returns>
    public async Task<VariableCompileRequestDto> BuildAsync(
        Guid projectId,
        Guid workflowId,
        GraphDataModel graph,
        VariableDefUseAnalysisMode defUseMode = VariableDefUseAnalysisMode.Conservative
    )
    {
        List<string> sourceFlagErrors = WorkflowSourceFlagContractValidator.Validate(graph);
        if (sourceFlagErrors.Count > 0)
        {
            throw new UserFriendlyException(
                "工作流 source flags 校验失败：" + string.Join(" | ", sourceFlagErrors)
            );
        }

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
                        ExpectedTypeName = NormalizeExpectedTypeName(expectedTypeName),
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
                        ExpectedTypeName = NormalizeExpectedTypeName(expectedTypeName),
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

                string normalizedType = NormalizeExpectedTypeName(expectedTypeName)!;
                writes.Add(
                    new VariableReferenceDto
                    {
                        OwnerWorkflowId = workflowId,
                        VariableName = variableName,
                        Location = $"{node.Id}:outputBindings:{key}",
                        Sequence = sequenceMap.GetValueOrDefault(node.Id),
                        ExpectedTypeName = normalizedType,
                    }
                );

                if (
                    declarationMap.TryGetValue(variableName, out VariableDeclarationDto? existing)
                    && !string.Equals(existing.TypeName, normalizedType, StringComparison.Ordinal)
                )
                {
                    throw new UserFriendlyException(
                        $"变量 {variableName} 在图中被赋予了冲突类型：{existing.TypeName} 与 {normalizedType}。"
                    );
                }

                declarationMap[variableName] = new VariableDeclarationDto
                {
                    Name = variableName,
                    TypeName = normalizedType,
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
            DefUseAnalysisMode = defUseMode,
            SuppressedDiagnosticCodes = [],
        };
    }

    private static string? NormalizeExpectedTypeName(string? expectedTypeName)
    {
        if (string.IsNullOrWhiteSpace(expectedTypeName))
        {
            return null;
        }

        return WorkflowExecutionTypeNormalizer.NormalizeDeclaredType(expectedTypeName);
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
}
