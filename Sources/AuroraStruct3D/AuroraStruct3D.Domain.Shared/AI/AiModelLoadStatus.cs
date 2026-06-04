namespace AuroraStruct3D.AI;

/// <summary>
/// AI 模型加载状态。
/// </summary>
public enum AiModelLoadStatus
{
    /// <summary>未加载。</summary>
    Unloaded = 0,

    /// <summary>加载中。</summary>
    Loading = 1,

    /// <summary>已加载。</summary>
    Loaded = 2,

    /// <summary>加载失败。</summary>
    Failed = 3,
}
