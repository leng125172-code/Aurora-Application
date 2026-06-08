namespace AuroraStruct3D.AI;

/// <summary>
/// AI 模型转换偏好。
/// 记录用户在上传或编辑时主动选择的目标运行方式。
/// RKLLM 相关值仅为兼容历史数据保留，不再接受新的写入请求。
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
    /// 兼容保留值。
    /// 当前版本不再允许通过该偏好触发 RKLLM 转换，需直接上传 RKLLM 文件。
    /// </summary>
    ToRkllm = 3,
}
