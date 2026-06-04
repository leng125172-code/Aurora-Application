using System.ComponentModel.DataAnnotations;

namespace AuroraStruct3D.AI.Dtos;

/// <summary>
/// 上传 AI 模型输入 DTO。
/// </summary>
public class UploadAiModelInput
{
    /// <summary>模型名称。</summary>
    [Required]
    [MaxLength(AiModelConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    /// <summary>模型描述。</summary>
    [MaxLength(AiModelConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>转换偏好。</summary>
    public AiModelConversionPreference ConversionPreference { get; set; } =
        AiModelConversionPreference.Auto;

    /// <summary>系统推断的转换结果。</summary>
    public AiModelResolvedConversionType ResolvedConversionType { get; set; } =
        AiModelResolvedConversionType.Unknown;

    /// <summary>模型标识名称列表。</summary>
    public List<string> IdentifierNames { get; set; } = [];

    /// <summary>位置标识。</summary>
    [MaxLength(AiModelConsts.MaxLocationKeyLength)]
    public string? LocationKey { get; set; }

    /// <summary>生成条件。</summary>
    [MaxLength(AiModelConsts.MaxGenerationConditionLength)]
    public string? GenerationCondition { get; set; }

    /// <summary>模型文件列表。</summary>
    public List<AiModelUploadFileInput> Files { get; set; } = [];
}
