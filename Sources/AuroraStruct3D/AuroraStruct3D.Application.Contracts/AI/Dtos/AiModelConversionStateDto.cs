namespace AuroraStruct3D.AI.Dtos;

/// <summary>
/// AI 模型转换阶段状态快照 DTO。
/// </summary>
public class AiModelConversionStateDto
{
    /// <summary>模型 ID。</summary>
    public Guid ModelId { get; set; }

    /// <summary>本次参与转换的原始文件 ID 列表。</summary>
    public List<Guid> SourceFileIds { get; set; } = [];

    /// <summary>转换目标类型。</summary>
    public AiModelResolvedConversionType? TargetType { get; set; }

    /// <summary>当前转换阶段状态。</summary>
    public AiModelFileConversionStatus Status { get; set; }

    /// <summary>转换错误信息。无错误时固定返回 null。</summary>
    public string? ConversionErrorMessage { get; set; }

    /// <summary>最近一次状态更新时间。</summary>
    public DateTime? LastUpdatedTime { get; set; }
}
