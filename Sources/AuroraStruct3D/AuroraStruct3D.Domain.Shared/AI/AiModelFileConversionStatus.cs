namespace AuroraStruct3D.AI;

/// <summary>
/// AI 模型文件转换状态。
/// 用于标记转换产物当前所处的生命周期状态。
/// </summary>
public enum AiModelFileConversionStatus
{
    /// <summary>
    /// 不适用。
    /// 原始上传文件默认使用该状态。
    /// </summary>
    None = 0,

    /// <summary>
    /// 等待转换。
    /// </summary>
    Pending = 1,

    /// <summary>
    /// 转换中。
    /// </summary>
    Converting = 2,

    /// <summary>
    /// 转换成功。
    /// </summary>
    Completed = 3,

    /// <summary>
    /// 转换失败。
    /// </summary>
    Failed = 4,
}
