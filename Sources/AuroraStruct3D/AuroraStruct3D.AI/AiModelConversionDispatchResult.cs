namespace AuroraStruct3D.AI;

/// <summary>
/// AI 模型转换调度结果。
/// </summary>
public sealed class AiModelConversionDispatchResult
{
    /// <summary>是否成功入队。</summary>
    public bool Queued { get; init; }

    /// <summary>转换状态。</summary>
    public AiModelFileConversionStatus Status { get; init; }

    /// <summary>结果消息。</summary>
    public string Message { get; init; } = string.Empty;
}
