namespace AuroraStruct3D.Workflow.Dtos;

/// <summary>
/// 单个节点的完整定义，用于工作流编辑器的节点面板展示与节点实例化。
/// </summary>
public sealed class NodeDefinitionDto
{
    /// <summary>
    /// 节点唯一标识：
    /// <list type="bullet">
    ///   <item>算子节点：GUID 字符串（来自 [Guid] 特性），如 <c>"7544f3f3-040d-4571-b0f2-741c8f17ab41"</c></item>
    ///   <item>内置结构节点：前缀 <c>"builtin::"</c>，如 <c>"builtin::for_loop"</c></item>
    /// </list>
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// 节点类型标识，前端据此决定节点的渲染逻辑和序列化格式：
    /// <list type="bullet">
    ///   <item><c>"Operator"</c> — 算子调用</item>
    ///   <item><c>"ForLoop"</c> — for 循环</item>
    ///   <item><c>"IfElse"</c> — 条件分支</item>
    ///   <item><c>"Assign"</c> — 变量赋值</item>
    /// </list>
    /// </summary>
    public required string NodeType { get; init; }

    /// <summary>UI 显示名，来自 [DisplayName] 特性或内置定义。</summary>
    public required string DisplayName { get; init; }

    /// <summary>功能描述，来自 [Description] 特性或内置定义，可为 null。</summary>
    public string? Description { get; init; }

    /// <summary>
    /// 是否包含子语句体（子节点列表）。
    /// <c>true</c>：for 循环、if/else 等容器节点；
    /// <c>false</c>：算子调用、赋值等叶节点。
    /// </summary>
    public bool HasBody { get; init; }

    /// <summary>
    /// 是否为流程边界节点（start-node / end-node）。
    /// <c>true</c>：单例边界节点，编辑器应自动放置、不可重复添加、不可删除，且不作为普通可拖项；
    /// <c>false</c>：普通可拖拽节点。
    /// </summary>
    public bool IsBoundary { get; init; }

    /// <summary>输入端口列表，顺序与算子 InputVisionParameters 定义一致。</summary>
    public required IReadOnlyList<NodePortDto> InputPorts { get; init; }

    /// <summary>输出端口列表，顺序与算子 OutputVisionParameters 定义一致。</summary>
    public required IReadOnlyList<NodePortDto> OutputPorts { get; init; }

    /// <summary>
    /// 算法配置字段列表（对应算子构造函数参数）。
    /// 内置结构节点（ForLoop、IfElse 等）使用此字段描述其参数（循环变量名、范围等）。
    /// </summary>
    public required IReadOnlyList<NodeConfigFieldDto> ConfigFields { get; init; }
}
