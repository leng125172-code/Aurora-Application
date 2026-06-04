namespace AuroraStruct3D.AI.Dtos;

/// <summary>
/// 启动 AI 模型转换结果 DTO。
/// </summary>
public class AiModelConversionStartResultDto
{
    /// <summary>模型 ID。</summary>
    public Guid ModelId { get; set; }

    /// <summary>用户选择的转换偏好。</summary>
    public AiModelConversionPreference ConversionPreference { get; set; }

    /// <summary>系统解析后的转换目标。</summary>
    public AiModelResolvedConversionType ResolvedConversionType { get; set; }

    /// <summary>是否允许进入转换流程。</summary>
    public bool CanConvert { get; set; }

    /// <summary>是否已成功入队。</summary>
    public bool Queued { get; set; }

    /// <summary>当前状态。</summary>
    public AiModelFileConversionStatus Status { get; set; }

    /// <summary>提示消息。</summary>
    public string Message { get; set; } = string.Empty;
}
