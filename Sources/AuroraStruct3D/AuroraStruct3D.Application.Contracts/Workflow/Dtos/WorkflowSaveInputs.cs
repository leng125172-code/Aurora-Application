using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace AuroraStruct3D.Workflow.Dtos;

/// <summary>
/// 新建工作流请求。
/// </summary>
public class CreateWorkflowInput
{
    /// <summary>所属项目 ID。</summary>
    [Required]
    public Guid ProjectId { get; set; }

    /// <summary>工作流名称。</summary>
    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    /// <summary>工作流图数据。</summary>
    [Required]
    public WorkflowGraphDto GraphData { get; set; } = new();
}

/// <summary>
/// 更新工作流请求。
/// </summary>
public class UpdateWorkflowInput
{
    /// <summary>所属项目 ID。</summary>
    [Required]
    public Guid ProjectId { get; set; }

    /// <summary>工作流名称。</summary>
    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    /// <summary>工作流图数据。</summary>
    [Required]
    public WorkflowGraphDto GraphData { get; set; } = new();
}

/// <summary>
/// 工作流 graphData 请求模型。
/// </summary>
public class WorkflowGraphDto
{
    /// <summary>本层节点集合。</summary>
    public List<WorkflowNodeDto> Nodes { get; set; } = new();

    /// <summary>本层连线集合。</summary>
    public List<WorkflowEdgeDto> Edges { get; set; } = new();
}

/// <summary>
/// 工作流节点。
/// </summary>
public class WorkflowNodeDto
{
    /// <summary>节点实例唯一 ID。</summary>
    [Required]
    public string Id { get; set; } = string.Empty;

    /// <summary>节点类型。</summary>
    [Required]
    public string Type { get; set; } = string.Empty;

    /// <summary>节点 X 坐标（画布定位）。</summary>
    public double? X { get; set; }

    /// <summary>节点 Y 坐标（画布定位）。</summary>
    public double? Y { get; set; }

    /// <summary>节点文本。</summary>
    public WorkflowNodeTextDto? Text { get; set; }

    /// <summary>节点属性。</summary>
    public WorkflowNodePropertiesDto? Properties { get; set; }
}

/// <summary>
/// 节点文本。
/// </summary>
public class WorkflowNodeTextDto
{
    /// <summary>文本 X 坐标（画布定位）。</summary>
    public double? X { get; set; }

    /// <summary>文本 Y 坐标（画布定位）。</summary>
    public double? Y { get; set; }

    /// <summary>显示文本。</summary>
    public string? Value { get; set; }
}

/// <summary>
/// 节点属性。
/// </summary>
public class WorkflowNodePropertiesDto
{
    /// <summary>参数值：参数名 -> 原始 JSON。</summary>
    public Dictionary<string, JsonElement>? Params { get; set; }

    /// <summary>参数来源：参数名 -> literal/variable。</summary>
    public Dictionary<string, string>? ParamSources { get; set; }

    /// <summary>输入绑定：端口名 -> 绑定值。</summary>
    public Dictionary<string, string>? InputBindings { get; set; }

    /// <summary>输入绑定来源：端口名 -> literal/variable。</summary>
    public Dictionary<string, string>? InputBindingSources { get; set; }

    /// <summary>输出绑定：端口名 -> 变量名。</summary>
    public Dictionary<string, string>? OutputBindings { get; set; }

    /// <summary>输出绑定来源：端口名 -> literal/variable。</summary>
    public Dictionary<string, string>? OutputBindingSources { get; set; }

    /// <summary>容器子图。</summary>
    public WorkflowGraphDto? InnerGraphData { get; set; }
}

/// <summary>
/// 工作流连线。
/// </summary>
public class WorkflowEdgeDto
{
    /// <summary>连线唯一 ID。</summary>
    [Required]
    public string Id { get; set; } = string.Empty;

    /// <summary>源节点 ID。</summary>
    public string? SourceNodeId { get; set; }

    /// <summary>目标节点 ID。</summary>
    public string? TargetNodeId { get; set; }

    /// <summary>源锚点索引。</summary>
    public int? SourceAnchorIndex { get; set; }

    /// <summary>目标锚点索引。</summary>
    public int? TargetAnchorIndex { get; set; }

    /// <summary>连线属性。</summary>
    public WorkflowEdgePropertiesDto? Properties { get; set; }
}

/// <summary>
/// 连线属性。
/// </summary>
public class WorkflowEdgePropertiesDto
{
    /// <summary>分支标记。</summary>
    public string? Branch { get; set; }
}
