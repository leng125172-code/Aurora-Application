using System.ComponentModel.DataAnnotations;

namespace AuroraStruct3D.AI.Dtos;

/// <summary>
/// 更新 AI 模型元数据输入 DTO。
/// </summary>
public class UpdateAiModelInput
{
    /// <summary>模型名称。</summary>
    [Required]
    [MaxLength(AiModelConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    /// <summary>模型描述。</summary>
    [MaxLength(AiModelConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>
    /// 转换偏好。
    /// 仅支持 Auto、DirectOnnx 和 ToRknn；ToRkllm 仅保留为兼容历史值。
    /// </summary>
    public AiModelConversionPreference ConversionPreference { get; set; } =
        AiModelConversionPreference.Auto;

    /// <summary>
    /// 系统推断的转换结果。
    /// 编辑请求中应保持 Unknown，实际解析结果仅在 Auto 模式下由系统更新。
    /// </summary>
    public AiModelResolvedConversionType ResolvedConversionType { get; set; } =
        AiModelResolvedConversionType.Unknown;

    /// <summary>模型标识名称列表。</summary>
    public List<string> IdentifierNames { get; set; } = [];

    /// <summary>模型版本。</summary>
    [MaxLength(AiModelConsts.MaxVersionLength)]
    public string? Version { get; set; }

    /// <summary>位置标识。</summary>
    [MaxLength(AiModelConsts.MaxLocationKeyLength)]
    public string? LocationKey { get; set; }

    /// <summary>生成条件。</summary>
    [MaxLength(AiModelConsts.MaxGenerationConditionLength)]
    public string? GenerationCondition { get; set; }

    /// <summary>文件列表编辑输入。</summary>
    public List<AiModelFileUpdateInput> Files { get; set; } = [];
}
