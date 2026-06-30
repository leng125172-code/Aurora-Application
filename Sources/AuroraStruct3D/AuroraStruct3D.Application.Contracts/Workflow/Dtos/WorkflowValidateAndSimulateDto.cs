namespace AuroraStruct3D.Workflow.Dtos;

/// <summary>
/// 工作流校验结果 DTO。
/// </summary>
public class WorkflowValidateResultDto
{
    /// <summary>是否存在阻断编译的 Error。</summary>
    public bool HasErrors { get; set; }

    /// <summary>错误列表（阻断编译）。</summary>
    public List<WorkflowDiagnosticDto> Errors { get; set; } = new();

    /// <summary>警告列表（不阻断，提示潜在问题）。</summary>
    public List<WorkflowDiagnosticDto> Warnings { get; set; } = new();
}

/// <summary>
/// 单条校验诊断 DTO。
/// </summary>
public class WorkflowDiagnosticDto
{
    /// <summary>严重级别：Error / Warning。</summary>
    public string Severity { get; set; } = string.Empty;

    /// <summary>相关节点 ID。</summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>相关端口 / 参数 / 变量名。</summary>
    public string? Target { get; set; }

    /// <summary>诊断信息。</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// 数据流仿真报告 DTO。
/// </summary>
public class WorkflowDataFlowReportDto
{
    /// <summary>工作流名称。</summary>
    public string WorkflowName { get; set; } = string.Empty;

    /// <summary>节点总数。</summary>
    public int TotalNodeCount { get; set; }

    /// <summary>算子节点数。</summary>
    public int OperatorNodeCount { get; set; }

    /// <summary>变量总数。</summary>
    public int TotalVariableCount { get; set; }

    /// <summary>拓扑层级数。</summary>
    public int TopologyDepth { get; set; }

    /// <summary>每个节点的数据流摘要。</summary>
    public List<NodeDataFlowInfoDto> Nodes { get; set; } = new();

    /// <summary>每个变量的生命周期。</summary>
    public List<VariableLifecycleDto> Variables { get; set; } = new();
}

/// <summary>
/// 节点数据流摘要 DTO。
/// </summary>
public class NodeDataFlowInfoDto
{
    /// <summary>节点 ID。</summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>节点类型。</summary>
    public string NodeType { get; set; } = string.Empty;

    /// <summary>节点显示名。</summary>
    public string? DisplayName { get; set; }

    /// <summary>拓扑层级。</summary>
    public int TopologyLayer { get; set; }

    /// <summary>消耗的变量列表。</summary>
    public List<VariablePortRefDto> Consumes { get; set; } = new();

    /// <summary>产出的变量列表。</summary>
    public List<VariablePortRefDto> Produces { get; set; } = new();
}

/// <summary>
/// 变量端口绑定 DTO。
/// </summary>
public class VariablePortRefDto
{
    /// <summary>变量名。</summary>
    public string VariableName { get; set; } = string.Empty;

    /// <summary>端口名。</summary>
    public string PortName { get; set; } = string.Empty;

    /// <summary>类型名。</summary>
    public string? TypeName { get; set; }
}

/// <summary>
/// 变量生命周期 DTO。
/// </summary>
public class VariableLifecycleDto
{
    /// <summary>变量名。</summary>
    public string VariableName { get; set; } = string.Empty;

    /// <summary>变量类型。</summary>
    public string? TypeName { get; set; }

    /// <summary>定义该变量的节点 ID。</summary>
    public string DefinedByNodeId { get; set; } = string.Empty;

    /// <summary>消费该变量的节点 ID 列表。</summary>
    public List<string> ConsumedByNodeIds { get; set; } = new();
}