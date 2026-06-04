using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.AI;

/// <summary>
/// AI 模型聚合根。
/// 记录模型仓库中的模型级元数据、转换类型、位置标识与生成条件。
/// </summary>
public class AiModel : FullAuditedAggregateRoot<Guid>
{
    /// <summary>模型显示名称。</summary>
    public string Name { get; private set; } = null!;

    /// <summary>模型描述。</summary>
    public string? Description { get; private set; }

    /// <summary>模型版本。</summary>
    public string? Version { get; private set; }

    /// <summary>模型位置/节点标识。</summary>
    public string? LocationKey { get; private set; }

    /// <summary>生成条件描述。</summary>
    public string? GenerationCondition { get; private set; }

    /// <summary>用户选择的转换类型。</summary>
    public AiModelConversionPreference ConversionPreference { get; private set; }

    /// <summary>系统推断出的转换结果。</summary>
    public AiModelResolvedConversionType ResolvedConversionType { get; private set; }

    /// <summary>文件数量。</summary>
    public int FileCount { get; private set; }

    /// <summary>EF Core 使用的无参构造函数。</summary>
    protected AiModel() { }

    /// <summary>
    /// 创建新的 AI 模型记录。
    /// </summary>
    public static AiModel Create(
        Guid id,
        string name,
        string? description = null,
        string? version = null,
        string? locationKey = null,
        string? generationCondition = null,
        AiModelConversionPreference conversionPreference = AiModelConversionPreference.Auto,
        AiModelResolvedConversionType resolvedConversionType =
            AiModelResolvedConversionType.Unknown,
        int fileCount = 0
    )
    {
        return new AiModel
        {
            Id = id,
            Name = Check.NotNullOrWhiteSpace(name, nameof(name), AiModelConsts.MaxNameLength),
            Description = TrimOrNull(description, AiModelConsts.MaxDescriptionLength),
            Version = TrimOrNull(version, AiModelConsts.MaxVersionLength),
            LocationKey = TrimOrNull(locationKey, AiModelConsts.MaxLocationKeyLength),
            GenerationCondition = TrimOrNull(
                generationCondition,
                AiModelConsts.MaxGenerationConditionLength
            ),
            ConversionPreference = conversionPreference,
            ResolvedConversionType = resolvedConversionType,
            FileCount = fileCount,
        };
    }

    /// <summary>
    /// 更新模型元数据。
    /// </summary>
    public void UpdateMetadata(
        string name,
        string? description,
        string? version,
        string? locationKey,
        string? generationCondition,
        AiModelConversionPreference conversionPreference,
        AiModelResolvedConversionType resolvedConversionType
    )
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), AiModelConsts.MaxNameLength);
        Description = TrimOrNull(description, AiModelConsts.MaxDescriptionLength);
        Version = TrimOrNull(version, AiModelConsts.MaxVersionLength);
        LocationKey = TrimOrNull(locationKey, AiModelConsts.MaxLocationKeyLength);
        GenerationCondition = TrimOrNull(
            generationCondition,
            AiModelConsts.MaxGenerationConditionLength
        );
        ConversionPreference = conversionPreference;
        ResolvedConversionType = resolvedConversionType;
    }

    /// <summary>
    /// 更新文件数量。
    /// </summary>
    public void UpdateFileCount(int fileCount)
    {
        if (fileCount < 0 || fileCount > AiModelConsts.MaxFileCountPerModel)
        {
            throw new BusinessException("AiModel:InvalidFileCount");
        }

        FileCount = fileCount;
    }

    private static string? TrimOrNull(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
