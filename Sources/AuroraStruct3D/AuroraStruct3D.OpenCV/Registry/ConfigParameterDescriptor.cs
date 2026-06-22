namespace AuroraStruct3D.OpenCV.Registry;

/// <summary>
/// 算子构造函数配置参数的序列化描述，用于 Redis 缓存和前端接口响应。
/// 对应 <see cref="AuroraStruct3D.OpenCV.VisionParameters.IConfigParameter"/>。
/// </summary>
public sealed class ConfigParameterDescriptor
{
    /// <summary>构造函数参数名，与算子 C# 构造函数参数名一致。</summary>
    public required string Name { get; init; }

    /// <summary>UI 显示名，可为 null（前端回退到 Name）。</summary>
    public string? DisplayName { get; init; }

    /// <summary>参数 CLR 类型全名（含命名空间）。</summary>
    public required string ParameterTypeName { get; init; }

    /// <summary>
    /// 默认值，枚举类型序列化为名称字符串（如 "Color"），
    /// 数值类型保留原始值，可为 null。
    /// </summary>
    public object? DefaultValue { get; init; }

    /// <summary>
    /// 值范围约束：枚举类型为名称字符串数组；数值类型为 [Min, Max]；可为 null。
    /// </summary>
    public object? ValueLimit { get; init; }

    /// <summary>是否为必填参数。</summary>
    public bool Required { get; init; }

    /// <summary>前端控件类型，用于决定渲染何种 UI 控件。</summary>
    public PortControlType ControlType { get; init; }
}
