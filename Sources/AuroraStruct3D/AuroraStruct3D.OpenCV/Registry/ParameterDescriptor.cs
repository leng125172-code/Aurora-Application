namespace AuroraStruct3D.OpenCV.Registry;

/// <summary>
/// 算子端口（入参或出参）的静态描述，来自算子类的
/// <c>InputVisionParameters</c> / <c>OutputVisionParameters</c> 静态属性。
/// </summary>
public sealed class ParameterDescriptor
{
    /// <summary>
    /// 端口变量名，即连线路由唯一标识符。
    /// 对应 <see cref="IVisionParameter.ParameterName"/>。
    /// </summary>
    public string? ParameterName { get; init; }

    /// <summary>
    /// 端口实例级 UI 显示名。为 null 时工作流引擎回退到参数类型上的 [DisplayName] 特性。
    /// 同一算子有多个相同类型端口时（如双目相机），应使用此字段区分（如"左图"/"右图"）。
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>端口配置指南，说明如何连接、默认值、范围及输出使用方式；接口响应中保证非空。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>参数值的 CLR 类型全名，用于引擎进行连线类型兼容性校验。</summary>
    public required string ParameterTypeName { get; init; }

    /// <summary>默认值，序列化为 JSON，可为 null。</summary>
    public object? DefaultValue { get; init; }

    /// <summary>
    /// 值范围约束，序列化为 JSON，可为 null。
    /// 文件路径类型为扩展名数组；数值类型为 [Min, Max] 数组；枚举为枚举值列表。
    /// </summary>
    public object? ValueLimit { get; init; }

    /// <summary>结构化 JSON 输出的 JSON Schema。</summary>
    public string? JsonSchema { get; init; }

    /// <summary>是否在赋值时进行合法性校验。</summary>
    public bool ErrorCheck { get; init; }

    /// <summary>前端控件类型，用于决定渲染何种 UI 控件。</summary>
    public PortControlType ControlType { get; init; }

    /// <summary>矩阵类型，用于区分 2D 图像矩阵和 3D 点云坐标矩阵。</summary>
    public PortMatType MatType { get; init; }
}
