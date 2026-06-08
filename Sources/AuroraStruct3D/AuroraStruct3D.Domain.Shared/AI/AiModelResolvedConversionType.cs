namespace AuroraStruct3D.AI;

/// <summary>
/// AI 模型最终解析得到的转换类型。
/// 用于记录系统根据平台和文件元数据推断出的建议结果。
/// 该字段仅在转换偏好为 Auto 时具有业务含义。
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
    /// 表示系统分析认为模型更适合 RKLLM/NPU 运行。
    /// 当前版本不再执行 ONNX 到 RKLLM 的自动转换。
    /// </summary>
    ToRkllm = 3,
}
