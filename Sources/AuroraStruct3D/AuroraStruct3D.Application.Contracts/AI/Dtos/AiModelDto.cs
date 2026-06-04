using Volo.Abp.Application.Dtos;

namespace AuroraStruct3D.AI.Dtos;

/// <summary>
/// AI 模型输出 DTO。
/// </summary>
public class AiModelDto : FullAuditedEntityDto<Guid>
{
    /// <summary>模型名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>模型描述。</summary>
    public string? Description { get; set; }

    /// <summary>模型标识列表。</summary>
    public List<AiModelIdentifierDto> ModelIdentifiers { get; set; } = [];

    /// <summary>模型版本。</summary>
    public string? Version { get; set; }

    /// <summary>位置标识。</summary>
    public string? LocationKey { get; set; }

    /// <summary>生成条件。</summary>
    public string? GenerationCondition { get; set; }

    /// <summary>用户选择的转换类型。</summary>
    public AiModelConversionPreference ConversionPreference { get; set; }

    /// <summary>系统解析得到的转换类型。</summary>
    public AiModelResolvedConversionType ResolvedConversionType { get; set; }

    /// <summary>文件数量。</summary>
    public int FileCount { get; set; }

    /// <summary>模型文件列表。</summary>
    public List<AiModelFileDto> Files { get; set; } = [];

    /// <summary>加载状态。</summary>
    public AiModelLoadStatus LoadStatus { get; set; }

    /// <summary>上传人用户名。</summary>
    public string? CreatorUserName { get; set; }
}

/// <summary>
/// AI 模型操作日志输出 DTO。
/// </summary>
public class AiModelOperationLogDto : EntityDto<Guid>
{
    /// <summary>关联的模型 ID。</summary>
    public Guid? AiModelId { get; set; }

    /// <summary>模型名称快照。</summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>原始文件名快照。</summary>
    public string? OriginalFileName { get; set; }

    /// <summary>操作类型。</summary>
    public AiModelOperationType OperationType { get; set; }

    /// <summary>操作发生时间。</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>是否成功。</summary>
    public bool IsSuccess { get; set; }

    /// <summary>参数摘要。</summary>
    public string? ParameterSummary { get; set; }

    /// <summary>错误信息。</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>耗时（毫秒）。</summary>
    public long DurationMs { get; set; }
}
