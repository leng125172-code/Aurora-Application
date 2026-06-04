namespace AuroraStruct3D.AI;

/// <summary>
/// AI 模型转换偏好。
/// 记录用户在上传或编辑时主动选择的目标运行方式。
/// </summary>
public enum AiModelConversionPreference
{
    /// <summary>
    /// 自动判断。
    /// </summary>
    Auto = 0,

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
