namespace AuroraStruct3D.AI;

/// <summary>
/// AI 模型最终解析得到的转换类型。
/// 用于记录系统根据平台和文件元数据推断出的建议结果。
/// </summary>
public enum AiModelResolvedConversionType
{
    /// <summary>
    /// 未知。
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 使用 ONNX 直接运行。
    /// </summary>
    DirectOnnx = 1,

    /// <summary>
    /// 转换为 RKNN 运行。
    /// </summary>
    ToRknn = 2,

    /// <summary>
    /// 转换为 RKLLM 运行。
    /// </summary>
    ToRkllm = 3,
}
