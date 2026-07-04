using System.Text.Json;
using System.Text.Json.Serialization;

namespace AuroraStruct3D.OpenCV.Workflow.Compilation.Model;

/// <summary>
/// 持久化 <c>graphData</c> 的反序列化模型（一个作用域层：根画布或某容器内层）。
/// 对应 workflow-json-spec.md 第 2 节。
/// </summary>
public sealed class GraphDataModel
{
    /// <summary>本层节点集合。</summary>
    public List<NodeModel> Nodes { get; set; } = new();

    /// <summary>本层连线集合（仅连接本层直接节点）。</summary>
    public List<EdgeModel> Edges { get; set; } = new();
}

/// <summary>工作流节点（算子 / 容器 / start-node / end-node / builtin::assign）。</summary>
public sealed class NodeModel
{
    /// <summary>节点实例唯一 ID。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 节点类型：算子 UUID、<c>flow_container</c>、<c>start-node</c>、<c>end-node</c>、
    /// 或内置节点 ID（如 <c>builtin::assign</c>）。
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>节点 X 坐标（画布定位）。</summary>
    public double? X { get; set; }

    /// <summary>节点 Y 坐标（画布定位）。</summary>
    public double? Y { get; set; }

    /// <summary>节点显示名（可选）。</summary>
    public NodeTextModel? Text { get; set; }

    /// <summary>节点业务数据（三段式 + 容器子图）。</summary>
    public NodePropertiesModel? Properties { get; set; }
}

/// <summary>节点显示名包装。</summary>
public sealed class NodeTextModel
{
    /// <summary>文本 X 坐标（画布定位）。</summary>
    public double? X { get; set; }

    /// <summary>文本 Y 坐标（画布定位）。</summary>
    public double? Y { get; set; }

    /// <summary>显示文本。</summary>
    public string? Value { get; set; }
}

/// <summary>
/// 节点 <c>properties</c> 三段式结构 + 容器子图。对应 workflow-json-spec.md 第 4 节。
/// </summary>
public sealed class NodePropertiesModel
{
    /// <summary>
    /// 算子运行参数（configFields）。值为字面量，或变量引用哨兵
    /// <c>{ "$var": "name" }</c>。保留为 <see cref="JsonElement"/> 以延迟解析。
    /// </summary>
    public Dictionary<string, JsonElement>? Params { get; set; }

    /// <summary>
    /// 配置参数来源标记：参数名 → <c>literal</c> / <c>variable</c>。
    /// </summary>
    public Dictionary<string, string>? ParamSources { get; set; }

    /// <summary>输入端口绑定：端口名 → 上游变量名。</summary>
    public Dictionary<string, string>? InputBindings { get; set; }

    /// <summary>
    /// 输入端口绑定来源标记：端口名 → <c>literal</c> / <c>variable</c>。
    /// </summary>
    public Dictionary<string, string>? InputBindingSources { get; set; }

    /// <summary>输出端口绑定：端口名 → 本节点产出变量名。</summary>
    public Dictionary<string, string>? OutputBindings { get; set; }

    /// <summary>
    /// 输出端口绑定来源标记：端口名 → <c>literal</c> / <c>variable</c>。
    /// </summary>
    public Dictionary<string, string>? OutputBindingSources { get; set; }

    /// <summary>容器子画布（仅 <c>flow_container</c> 携带）。</summary>
    public GraphDataModel? InnerGraphData { get; set; }
}

/// <summary>工作流连线。</summary>
public sealed class EdgeModel
{
    /// <summary>连线唯一 ID。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>源节点 ID。</summary>
    public string? SourceNodeId { get; set; }

    /// <summary>目标节点 ID。</summary>
    public string? TargetNodeId { get; set; }

    /// <summary>源端锚点索引（0=上 / 1=右 / 2=下 / 3=左，视觉路由用）。</summary>
    public int? SourceAnchorIndex { get; set; }

    /// <summary>目标端锚点索引（0=上 / 1=右 / 2=下 / 3=左，视觉路由用）。</summary>
    public int? TargetAnchorIndex { get; set; }

    /// <summary>连线业务数据（含条件分支标记）。</summary>
    public EdgePropertiesModel? Properties { get; set; }
}

/// <summary>连线 <c>properties</c>。</summary>
public sealed class EdgePropertiesModel
{
    /// <summary>条件分支取值：<c>yes</c> / <c>no</c> / <c>default</c>（决策节点出边）。</summary>
    public string? Branch { get; set; }
}

/// <summary>
/// <c>graphData</c> 反序列化的共享 JSON 配置：camelCase、大小写不敏感、忽略未知字段。
/// </summary>
public static class GraphJson
{
    /// <summary>编译器统一使用的反序列化选项。</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>从 params 中按键读取一个子图（用于 if/else 的 then/else 分支体）；缺失则返回空图。</summary>
    public static GraphDataModel ReadSubGraph(
        IReadOnlyDictionary<string, JsonElement> p,
        string key
    )
    {
        if (p.TryGetValue(key, out JsonElement el) && el.ValueKind == JsonValueKind.Object)
            return el.Deserialize<GraphDataModel>(Options) ?? new GraphDataModel();
        return new GraphDataModel();
    }
}
