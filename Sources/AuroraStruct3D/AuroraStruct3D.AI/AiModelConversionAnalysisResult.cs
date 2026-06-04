namespace AuroraStruct3D.AI;

/// <summary>
/// ONNX 模型分析结果。
/// </summary>
public sealed class AiModelConversionAnalysisResult
{
    /// <summary>是否允许进入转换流程。</summary>
    public bool CanConvert { get; init; }

    /// <summary>分析后得到的转换类型。</summary>
    public AiModelResolvedConversionType ResolvedConversionType { get; init; }

    /// <summary>分析摘要消息。</summary>
    public string Message { get; init; } = string.Empty;
}
