namespace AuroraStruct3D.OpenCV.Workflow.Compilation;

/// <summary>
/// 数据流仿真报告：汇总每个节点的数据流、变量生命周期、拓扑层级信息。
/// 不实际执行算子，仅模拟变量绑定与流动路径。
/// </summary>
public sealed class WorkflowDataFlowReport
{
    /// <summary>工作流名称。</summary>
    public string WorkflowName { get; init; } = string.Empty;

    /// <summary>节点总数（含起止节点）。</summary>
    public int TotalNodeCount { get; init; }

    /// <summary>算子节点数（不含起止 / 赋值 / 容器）。</summary>
    public int OperatorNodeCount { get; init; }

    /// <summary>变量总数。</summary>
    public int TotalVariableCount { get; init; }

    /// <summary>拓扑层级数（最深层级从 0 开始）。</summary>
    public int TopologyDepth { get; init; }

    /// <summary>每个节点的数据流摘要。</summary>
    public required IReadOnlyList<NodeDataFlowInfo> Nodes { get; init; }

    /// <summary>每个变量的生命周期。</summary>
    public required IReadOnlyList<VariableLifecycle> Variables { get; init; }
}

/// <summary>
/// 单个节点的数据流摘要：消耗/产出变量、类型、拓扑层级。
/// </summary>
public sealed class NodeDataFlowInfo
{
    /// <summary>节点 ID。</summary>
    public required string NodeId { get; init; }

    /// <summary>节点类型（算子 GUID / start-node / end-node / builtin::assign / flow_container）。</summary>
    public required string NodeType { get; init; }

    /// <summary>节点显示名。</summary>
    public string? DisplayName { get; init; }

    /// <summary>拓扑层级（0 为起始层）。</summary>
    public int TopologyLayer { get; init; }

    /// <summary>该节点消耗的变量列表（输入端口绑定）。</summary>
    public required IReadOnlyList<VariablePortRef> Consumes { get; init; }

    /// <summary>该节点产出的变量列表（输出端口绑定）。</summary>
    public required IReadOnlyList<VariablePortRef> Produces { get; init; }
}

/// <summary>
/// 变量与端口的绑定关系。
/// </summary>
public sealed class VariablePortRef
{
    /// <summary>变量名。</summary>
    public required string VariableName { get; init; }

    /// <summary>绑定端口名。</summary>
    public required string PortName { get; init; }

    /// <summary>变量类型（null 表示未知）。</summary>
    public string? TypeName { get; init; }
}

/// <summary>
/// 变量的完整生命周期：定义节点、消费节点列表、类型。
/// </summary>
public sealed class VariableLifecycle
{
    /// <summary>变量名。</summary>
    public required string VariableName { get; init; }

    /// <summary>变量类型（null 表示未知）。</summary>
    public string? TypeName { get; init; }

    /// <summary>定义该变量的节点 ID。</summary>
    public required string DefinedByNodeId { get; init; }

    /// <summary>消费该变量的节点 ID 列表。</summary>
    public required IReadOnlyList<string> ConsumedByNodeIds { get; init; }
}