namespace AuroraStruct3D.AI;

/// <summary>
/// AI 模型文件类型。
/// </summary>
public enum AiModelFileRole
{
    /// <summary>
    /// 单文件整合模型。
    /// </summary>
    SingleWholeModel = 0,

    /// <summary>
    /// 拆分模型-编码器。
    /// </summary>
    SplitEncoder = 1,

    /// <summary>
    /// 拆分模型-解码器。
    /// </summary>
    SplitDecoder = 2,
}
