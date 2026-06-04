using Volo.Abp.Application.Dtos;

namespace AuroraStruct3D.AI.Dtos;

/// <summary>
/// 查询 AI 模型列表输入 DTO。
/// </summary>
public class GetAiModelListInput : PagedAndSortedResultRequestDto
{
    /// <summary>关键字过滤。</summary>
    public string? Filter { get; set; }

    /// <summary>开始时间。</summary>
    public DateTime? StartTime { get; set; }

    /// <summary>结束时间。</summary>
    public DateTime? EndTime { get; set; }

    /// <summary>创建人用户 ID。</summary>
    public Guid? CreatorId { get; set; }

    /// <summary>位置标识。</summary>
    public string? LocationKey { get; set; }

    /// <summary>生成条件。</summary>
    public string? GenerationCondition { get; set; }

    /// <summary>用户选择的转换类型。</summary>
    public AiModelConversionPreference? ConversionPreference { get; set; }

    /// <summary>系统推断的转换结果。</summary>
    public AiModelResolvedConversionType? ResolvedConversionType { get; set; }

    /// <summary>文件类型。</summary>
    public AiModelFileRole? FileRole { get; set; }

    /// <summary>文件格式。</summary>
    public string? FileFormat { get; set; }

    /// <summary>加载状态。</summary>
    public AiModelLoadStatus? LoadStatus { get; set; }
}
