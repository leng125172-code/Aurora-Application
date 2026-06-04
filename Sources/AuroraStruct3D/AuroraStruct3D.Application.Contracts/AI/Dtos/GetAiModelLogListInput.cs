using Volo.Abp.Application.Dtos;

namespace AuroraStruct3D.AI.Dtos;

/// <summary>
/// 查询 AI 模型日志输入 DTO。
/// </summary>
public class GetAiModelLogListInput : PagedResultRequestDto
{
    /// <summary>模型 ID 过滤。</summary>
    public Guid? AiModelId { get; set; }

    /// <summary>关键字过滤。</summary>
    public string? Filter { get; set; }

    /// <summary>操作类型过滤。</summary>
    public AiModelOperationType? OperationType { get; set; }

    /// <summary>仅返回失败记录。</summary>
    public bool? IsFailedOnly { get; set; }

    /// <summary>开始时间。</summary>
    public DateTime? StartTime { get; set; }

    /// <summary>结束时间。</summary>
    public DateTime? EndTime { get; set; }
}
