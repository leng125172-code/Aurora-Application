using System.Text.Json;
using AuroraStruct3D.OpenCV.Registry;
using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;

namespace AuroraStruct3D.OpenCV.Workflow.Compilation;

/// <summary>
/// 数据流仿真引擎：不执行算子，仅按拓扑序遍历工作流图，模拟变量绑定与流动路径，
/// 生成 <see cref="WorkflowDataFlowReport"/> 供前端可视化。
/// <para>
/// 流程：反序列化 → 拓扑排序 → 逐节点收集消费/产出变量 → 汇总生命周期。
/// </para>
/// </summary>
public sealed class WorkflowDataFlowSimulator
{
    private readonly IOperatorRegistry _registry;

    /// <summary>变量定义记录：变量名 → (定义节点 ID, 类型)。</summary>
    private readonly Dictionary<string, (string NodeId, string? TypeName)> _definers = new(
        StringComparer.Ordinal
    );

    /// <summary>变量消费记录：变量名 → 消费它的节点 ID 列表。</summary>
    private readonly Dictionary<string, List<string>> _consumers = new(StringComparer.Ordinal);

    /// <summary>节点数据流信息列表。</summary>
    private readonly List<NodeDataFlowInfo> _nodeInfos = new();

    public WorkflowDataFlowSimulator(IOperatorRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    /// <summary>
    /// 对 graphData JSON 执行数据流仿真，生成报告。
    /// </summary>
    /// <param name="graphDataJson">graphData JSON 字符串。</param>
    /// <param name="workflowName">工作流名称。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<WorkflowDataFlowReport> SimulateAsync(
        string graphDataJson,
        string workflowName,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphDataJson);

        GraphDataModel graph =
            JsonSerializer.Deserialize<GraphDataModel>(graphDataJson, GraphJson.Options)
            ?? new GraphDataModel();

        return await SimulateAsync(graph, workflowName, cancellationToken);
    }

    /// <summary>
    /// 对 <see cref="GraphDataModel"/> 执行数据流仿真，生成报告。
    /// </summary>
    /// <param name="graph">已解析的图模型。</param>
    /// <param name="workflowName">工作流名称。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<WorkflowDataFlowReport> SimulateAsync(
        GraphDataModel graph,
        string workflowName,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(graph);

        _definers.Clear();
        _consumers.Clear();
        _nodeInfos.Clear();

        // ① 拓扑排序。
        IReadOnlyList<NodeModel> ordered;
        try
        {
            ordered = GraphTopology.Order(graph);
        }
        catch (WorkflowCompilationException)
        {
            // 拓扑排序失败（环 / 重复 ID），返回空报告。
            return new WorkflowDataFlowReport
            {
                WorkflowName = workflowName,
                TotalNodeCount = graph.Nodes.Count,
                OperatorNodeCount = 0,
                TotalVariableCount = 0,
                TopologyDepth = 0,
                Nodes = Array.Empty<NodeDataFlowInfo>(),
                Variables = Array.Empty<VariableLifecycle>(),
            };
        }

        // ② 计算每个节点的拓扑层级。
        var layerMap = ComputeTopologyLayers(graph, ordered);

        // ③ 逐节点收集数据流。
        foreach (NodeModel node in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await CollectNodeDataFlowAsync(node, layerMap, cancellationToken);
        }

        // ④ 组装变量生命周期。
        var variables = new List<VariableLifecycle>();
        foreach ((string varName, (string nodeId, string? typeName)) in _definers)
        {
            variables.Add(
                new VariableLifecycle
                {
                    VariableName = varName,
                    TypeName = typeName,
                    DefinedByNodeId = nodeId,
                    ConsumedByNodeIds = _consumers.TryGetValue(
                        varName,
                        out List<string>? consumerList
                    )
                        ? consumerList.AsReadOnly()
                        : Array.Empty<string>(),
                }
            );
        }

        int operatorCount = ordered.Count(n =>
            n.Type is not (NodeTypeTokens.StartNode or NodeTypeTokens.EndNode)
        );

        return new WorkflowDataFlowReport
        {
            WorkflowName = workflowName,
            TotalNodeCount = graph.Nodes.Count,
            OperatorNodeCount = operatorCount,
            TotalVariableCount = _definers.Count,
            TopologyDepth = layerMap.Values.Count > 0 ? layerMap.Values.Max() : 0,
            Nodes = _nodeInfos.AsReadOnly(),
            Variables = variables.AsReadOnly(),
        };
    }

    // ── 内部实现 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 基于拓扑排序结果计算每个节点的层级（最长路径深度）。
    /// </summary>
    private static Dictionary<string, int> ComputeTopologyLayers(
        GraphDataModel graph,
        IReadOnlyList<NodeModel> ordered
    )
    {
        var layer = new Dictionary<string, int>(StringComparer.Ordinal);

        // 构建入边映射：target → [(source1, source2, ...)]
        var predecessors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (EdgeModel edge in graph.Edges)
        {
            if (edge.SourceNodeId is null || edge.TargetNodeId is null)
                continue;
            if (!predecessors.TryGetValue(edge.TargetNodeId, out List<string>? preds))
            {
                preds = new List<string>();
                predecessors[edge.TargetNodeId] = preds;
            }
            preds.Add(edge.SourceNodeId);
        }

        foreach (NodeModel node in ordered)
        {
            int maxPredLayer = -1;
            if (predecessors.TryGetValue(node.Id, out List<string>? preds))
            {
                foreach (string predId in preds)
                {
                    if (layer.TryGetValue(predId, out int predLayer) && predLayer > maxPredLayer)
                        maxPredLayer = predLayer;
                }
            }

            layer[node.Id] = maxPredLayer + 1;
        }

        return layer;
    }

    private async Task CollectNodeDataFlowAsync(
        NodeModel node,
        Dictionary<string, int> layerMap,
        CancellationToken cancellationToken
    )
    {
        int layer = layerMap.GetValueOrDefault(node.Id, 0);

        switch (node.Type)
        {
            case NodeTypeTokens.StartNode:
            case NodeTypeTokens.EndNode:
                _nodeInfos.Add(
                    new NodeDataFlowInfo
                    {
                        NodeId = node.Id,
                        NodeType = node.Type,
                        DisplayName = node.Text?.Value,
                        TopologyLayer = layer,
                        Consumes = Array.Empty<VariablePortRef>(),
                        Produces = Array.Empty<VariablePortRef>(),
                    }
                );
                return;

            case NodeTypeTokens.Assign:
                CollectAssignFlow(node, layer);
                return;

            case NodeTypeTokens.FlowContainer:
                await CollectContainerFlowAsync(node, layer, cancellationToken);
                return;

            default:
                if (Guid.TryParse(node.Type, out Guid operatorId))
                    await CollectOperatorFlowAsync(node, operatorId, layer, cancellationToken);
                else
                    _nodeInfos.Add(
                        new NodeDataFlowInfo
                        {
                            NodeId = node.Id,
                            NodeType = node.Type,
                            DisplayName = node.Text?.Value,
                            TopologyLayer = layer,
                            Consumes = Array.Empty<VariablePortRef>(),
                            Produces = Array.Empty<VariablePortRef>(),
                        }
                    );
                return;
        }
    }

    private void CollectAssignFlow(NodeModel node, int layer)
    {
        Dictionary<string, JsonElement> p = node.Properties?.Params ?? new();
        string? variableName = GetString(p, "variableName");

        // 消费：value 中的变量引用。
        var consumes = new List<VariablePortRef>();
        if (p.TryGetValue("value", out JsonElement value))
        {
            if (
                !BindingSource.TryResolveSource(
                    node.Properties?.ParamSources,
                    "value",
                    out bool isVariable,
                    out string sourceError
                )
            )
            {
                throw new InvalidOperationException(
                    $"节点 {node.Id} 的 params['value'] source 无效：{sourceError}。"
                );
            }

            if (isVariable)
            {
                if (
                    !BindingSource.TryGetParamVariableNameStrict(
                        node.Properties?.ParamSources,
                        "value",
                        value,
                        out string refName,
                        out string variableError
                    )
                )
                {
                    throw new InvalidOperationException(
                        $"节点 {node.Id} 的 params['value'] 变量引用无效：{variableError}。"
                    );
                }

                consumes.Add(
                    new VariablePortRef
                    {
                        VariableName = refName,
                        PortName = "value",
                        TypeName = null,
                    }
                );
                RecordConsumer(refName, node.Id);
            }
        }

        // 产出。
        var produces = new List<VariablePortRef>();
        if (!string.IsNullOrWhiteSpace(variableName))
        {
            produces.Add(
                new VariablePortRef
                {
                    VariableName = variableName!,
                    PortName = "out",
                    TypeName = null,
                }
            );
            RecordDefiner(variableName!, node.Id, null);
        }

        _nodeInfos.Add(
            new NodeDataFlowInfo
            {
                NodeId = node.Id,
                NodeType = node.Type,
                DisplayName = node.Text?.Value,
                TopologyLayer = layer,
                Consumes = consumes.AsReadOnly(),
                Produces = produces.AsReadOnly(),
            }
        );
    }

    private async Task CollectContainerFlowAsync(
        NodeModel node,
        int layer,
        CancellationToken cancellationToken
    )
    {
        // 递归收集容器内层数据流。
        List<NodeDataFlowInfo> innerInfos = new();
        GraphDataModel inner = node.Properties?.InnerGraphData ?? new();
        if (inner.Nodes.Count > 0)
        {
            IReadOnlyList<NodeModel> innerOrdered;
            try
            {
                innerOrdered = GraphTopology.Order(inner);
            }
            catch
            {
                innerOrdered = inner.Nodes;
            }

            var innerLayers = ComputeTopologyLayers(inner, innerOrdered);
            foreach (NodeModel innerNode in innerOrdered)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await CollectNodeDataFlowAsync(innerNode, innerLayers, cancellationToken);
            }
        }

        // 容器自身的消费：无（参数从内部读取）。
        var consumes = new List<VariablePortRef>();

        // 容器自身的产出：outputBindings。
        var produces = new List<VariablePortRef>();
        if (node.Properties?.OutputBindings is { } outputs)
        {
            foreach ((string portName, string varName) in outputs)
            {
                if (
                    !BindingSource.TryResolveSource(
                        node.Properties.OutputBindingSources,
                        portName,
                        out bool isVariable,
                        out string sourceError
                    )
                )
                {
                    throw new InvalidOperationException(
                        $"节点 {node.Id} 的 outputBindings['{portName}'] source 无效：{sourceError}。"
                    );
                }

                if (!isVariable)
                {
                    continue;
                }

                produces.Add(
                    new VariablePortRef
                    {
                        VariableName = varName,
                        PortName = portName,
                        TypeName = null,
                    }
                );
                RecordDefiner(varName, node.Id, null);
            }
        }

        _nodeInfos.Add(
            new NodeDataFlowInfo
            {
                NodeId = node.Id,
                NodeType = node.Type,
                DisplayName = node.Text?.Value,
                TopologyLayer = layer,
                Consumes = consumes.AsReadOnly(),
                Produces = produces.AsReadOnly(),
            }
        );
    }

    private async Task CollectOperatorFlowAsync(
        NodeModel node,
        Guid operatorId,
        int layer,
        CancellationToken cancellationToken
    )
    {
        OperatorParametersDescriptor? descriptor = await _registry.GetParametersAsync(
            operatorId,
            cancellationToken
        );

        NodePropertiesModel props = node.Properties ?? new();

        // 消费 → 输入端口绑定。
        var consumes = new List<VariablePortRef>();
        if (props.InputBindings is { } inputs)
        {
            foreach ((string portName, string varName) in inputs)
            {
                if (
                    !BindingSource.TryResolveSource(
                        props.InputBindingSources,
                        portName,
                        out bool isVariable,
                        out string sourceError
                    )
                )
                {
                    throw new InvalidOperationException(
                        $"节点 {node.Id} 的 inputBindings['{portName}'] source 无效：{sourceError}。"
                    );
                }

                if (!isVariable)
                {
                    continue;
                }

                string? portType = descriptor
                    ?.Inputs.FirstOrDefault(i => i.ParameterName == portName)
                    ?.ParameterTypeName;
                consumes.Add(
                    new VariablePortRef
                    {
                        VariableName = varName,
                        PortName = portName,
                        TypeName = portType,
                    }
                );
                RecordConsumer(varName, node.Id);
            }
        }

        // 配置参数中的 $var 引用也视为消费。
        if (props.Params is { } prms && descriptor is not null)
        {
            foreach (ConfigParameterDescriptor cfg in descriptor.Config)
            {
                if (!prms.TryGetValue(cfg.Name, out JsonElement val))
                    continue;
                if (
                    !BindingSource.TryResolveSource(
                        props.ParamSources,
                        cfg.Name,
                        out bool isVariable,
                        out string sourceError
                    )
                )
                {
                    throw new InvalidOperationException(
                        $"节点 {node.Id} 的 params['{cfg.Name}'] source 无效：{sourceError}。"
                    );
                }

                if (!isVariable)
                {
                    continue;
                }

                if (
                    !BindingSource.TryGetParamVariableNameStrict(
                        props.ParamSources,
                        cfg.Name,
                        val,
                        out string refName,
                        out string variableError
                    )
                )
                {
                    throw new InvalidOperationException(
                        $"节点 {node.Id} 的 params['{cfg.Name}'] 变量引用无效：{variableError}。"
                    );
                }
                consumes.Add(
                    new VariablePortRef
                    {
                        VariableName = refName,
                        PortName = cfg.Name,
                        TypeName = cfg.ParameterTypeName,
                    }
                );
                RecordConsumer(refName, node.Id);
            }
        }

        // 产出 → 输出端口绑定。
        var produces = new List<VariablePortRef>();
        if (props.OutputBindings is { } outputs)
        {
            foreach ((string portName, string varName) in outputs)
            {
                if (
                    !BindingSource.TryResolveSource(
                        props.OutputBindingSources,
                        portName,
                        out bool isVariable,
                        out string sourceError
                    )
                )
                {
                    throw new InvalidOperationException(
                        $"节点 {node.Id} 的 outputBindings['{portName}'] source 无效：{sourceError}。"
                    );
                }

                if (!isVariable)
                {
                    continue;
                }

                string? portType = descriptor
                    ?.Outputs.FirstOrDefault(o => o.ParameterName == portName)
                    ?.ParameterTypeName;
                produces.Add(
                    new VariablePortRef
                    {
                        VariableName = varName,
                        PortName = portName,
                        TypeName = portType,
                    }
                );
                RecordDefiner(varName, node.Id, portType);
            }
        }

        _nodeInfos.Add(
            new NodeDataFlowInfo
            {
                NodeId = node.Id,
                NodeType = node.Type,
                DisplayName = node.Text?.Value,
                TopologyLayer = layer,
                Consumes = consumes.AsReadOnly(),
                Produces = produces.AsReadOnly(),
            }
        );
    }

    private void RecordDefiner(string varName, string nodeId, string? typeName)
    {
        _definers[varName] = (nodeId, typeName);
    }

    private void RecordConsumer(string varName, string nodeId)
    {
        if (!_consumers.TryGetValue(varName, out List<string>? list))
        {
            list = new List<string>();
            _consumers[varName] = list;
        }
        if (!list.Contains(nodeId))
            list.Add(nodeId);
    }

    private static string? GetString(Dictionary<string, JsonElement> p, string key) =>
        p.TryGetValue(key, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
