namespace AuroraStruct3D.Workflow.Dtos;

/// <summary>
/// 节点端口描述（输入或输出），对应工作流连线的一端。
/// </summary>
public sealed class NodePortDto
{
    /// <summary>
    /// 端口变量名（连线路由唯一 ID），对应 <c>IVisionParameter.ParameterName</c>。
    /// 例：<c>"img_path"</c>、<c>"output_mat"</c>。
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// 端口实例级 UI 显示名，为 null 时前端回退到端口类型上的 [DisplayName] 特性。
    /// 同一节点有多个同类型端口（如双目相机左右图）时使用。
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>入参或出参的配置指南，直接用于指导用户如何连接或使用该参数。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>端口数据类型 CLR 全名，用于连线时的类型兼容性校验。</summary>
    public required string PortTypeName { get; init; }

    /// <summary>默认值，可为 null。</summary>
    public object? DefaultValue { get; init; }

    /// <summary>
    /// 值范围约束：文件路径类型为允许扩展名数组；数值类型为 [Min, Max]；可为 null。
    /// </summary>
    public object? ValueLimit { get; init; }

    /// <summary>Structured port schema used by member completion and type checking.</summary>
    public string? JsonSchema { get; init; }

    /// <summary>IDE-friendly structural type of this port.</summary>
    public WorkflowTypeSymbolDto? TypeSymbol { get; init; }

    /// <summary>是否在赋值时进行类型/合法性校验。</summary>
    public bool ErrorCheck { get; init; }

    /// <summary>前端控件类型，用于决定渲染何种 UI 控件。</summary>
    public PortControlType ControlType { get; init; }

    /// <summary>矩阵类型，用于区分 2D 图像矩阵和 3D 点云坐标矩阵。</summary>
    public PortMatType MatType { get; init; }
}
