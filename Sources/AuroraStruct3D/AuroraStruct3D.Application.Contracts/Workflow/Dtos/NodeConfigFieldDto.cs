namespace AuroraStruct3D.Workflow.Dtos;

/// <summary>
/// 节点算法配置字段描述，对应算子构造函数参数（非工作流变量端口）。
/// 前端在将节点拖入画布时弹出配置面板，用户填写这些字段。
/// </summary>
public sealed class NodeConfigFieldDto
{
    /// <summary>构造函数参数名，区分大小写。</summary>
    public required string Name { get; init; }

    /// <summary>UI 显示名，可为 null（前端回退到 Name）。</summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// 参数 CLR 类型全名，前端据此渲染控件：
    /// <list type="bullet">
    ///   <item><c>System.String</c> → 文本框</item>
    ///   <item><c>System.Double</c> / <c>System.Int32</c> → 数字输入框</item>
    ///   <item>枚举类型 → 下拉选择框（选项来自 <see cref="ValueLimit"/>）</item>
    ///   <item><c>System.Boolean</c> → 开关</item>
    /// </list>
    /// </summary>
    public required string ValueTypeName { get; init; }

    /// <summary>默认值，枚举序列化为名称字符串，可为 null。</summary>
    public object? DefaultValue { get; init; }

    /// <summary>允许的值列表：枚举名称字符串数组；数值范围为 [Min, Max]；可为 null。</summary>
    public object? ValueLimit { get; init; }

    /// <summary>是否为必填字段（无默认值时应为 true）。</summary>
    public bool Required { get; init; }

    /// <summary>前端控件类型，用于决定渲染何种 UI 控件。</summary>
    public PortControlType ControlType { get; init; }
}
