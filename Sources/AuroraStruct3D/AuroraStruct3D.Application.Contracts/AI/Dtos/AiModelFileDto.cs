using Volo.Abp.Application.Dtos;

namespace AuroraStruct3D.AI.Dtos;

/// <summary>
/// AI 模型文件输出 DTO。
/// </summary>
public class AiModelFileDto : FullAuditedEntityDto<Guid>
{
    /// <summary>关联的模型 ID。</summary>
    public Guid AiModelId { get; set; }

    /// <summary>是否为原始上传文件。</summary>
    public bool IsOriginalFile { get; set; }

    /// <summary>是否为转换产物文件。</summary>
    public bool IsConvertedFile { get; set; }

    /// <summary>来源原始文件 ID。</summary>
    public Guid? SourceFileId { get; set; }

    /// <summary>原始上传文件名。</summary>
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>不带扩展名的显示名称。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>文件格式。</summary>
    public string FileFormat { get; set; } = string.Empty;

    /// <summary>文件大小（字节）。</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>文件 MD5。</summary>
    public string Md5 { get; set; } = string.Empty;

    /// <summary>文件角色。</summary>
    public AiModelFileRole FileRole { get; set; }

    /// <summary>MD5 校验是否通过。</summary>
    public bool Md5Verified { get; set; }

    /// <summary>同一模型下的显示顺序。</summary>
    public int SortOrder { get; set; }

    /// <summary>转换目标类型。</summary>
    public AiModelResolvedConversionType? ConversionTargetType { get; set; }

    /// <summary>转换状态。</summary>
    public AiModelFileConversionStatus ConversionStatus { get; set; }

    /// <summary>转换错误信息。</summary>
    public string? ConversionErrorMessage { get; set; }

    /// <summary>转换完成时间。</summary>
    public DateTime? ConversionTime { get; set; }
}
