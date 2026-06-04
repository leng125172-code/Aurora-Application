namespace AuroraStruct3D.AI.Jobs;

/// <summary>
/// AI 模型转换后台任务参数。
/// </summary>
public sealed class AiModelConversionJobArgs
{
    /// <summary>模型 ID。</summary>
    public Guid ModelId { get; init; }

    /// <summary>转换目标类型。</summary>
    public AiModelResolvedConversionType TargetType { get; init; }

    /// <summary>待转换原始文件 ID 列表。</summary>
    public IReadOnlyList<Guid> SourceFileIds { get; init; } = [];
}
