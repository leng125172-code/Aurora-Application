namespace AuroraStruct3D.OpenCV.Registry;

/// <summary>
/// 算子端口完整描述，包含该算子的全部入参和出参定义。
/// </summary>
public sealed class OperatorParametersDescriptor
{
    /// <summary>所属算子的唯一标识。</summary>
    public required Guid OperatorId { get; init; }

    /// <summary>
    /// 输入参数列表，顺序与算子 <c>InputVisionParameters</c> 定义一致。
    /// </summary>
    public required IReadOnlyList<ParameterDescriptor> Inputs { get; init; }

    /// <summary>
    /// 输出参数列表，顺序与算子 <c>OutputVisionParameters</c> 定义一致。
    /// </summary>
    public required IReadOnlyList<ParameterDescriptor> Outputs { get; init; }

    /// <summary>
    /// 构造函数配置参数列表，顺序与算子 <c>ConfigParameters</c> 定义一致。
    /// 无配置参数的算子此列表为空（不为 null）。
    /// </summary>
    public IReadOnlyList<ConfigParameterDescriptor> Config { get; init; } =
        Array.Empty<ConfigParameterDescriptor>();
}
