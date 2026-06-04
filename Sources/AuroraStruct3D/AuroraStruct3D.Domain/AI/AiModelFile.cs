using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.AI;

/// <summary>
/// AI 模型文件明细实体。
/// 一个模型可关联一个或多个物理文件。
/// </summary>
public class AiModelFile : FullAuditedAggregateRoot<Guid>
{
    /// <summary>关联的模型 ID。</summary>
    public Guid AiModelId { get; private set; }

    /// <summary>是否为原始上传文件。</summary>
    public bool IsOriginalFile { get; private set; }

    /// <summary>是否为转换产物文件。</summary>
    public bool IsConvertedFile { get; private set; }

    /// <summary>来源原始文件 ID。</summary>
    public Guid? SourceFileId { get; private set; }

    /// <summary>原始上传文件名。</summary>
    public string OriginalFileName { get; private set; } = null!;

    /// <summary>不带扩展名的显示名称。</summary>
    public string DisplayName { get; private set; } = null!;

    /// <summary>文件格式。</summary>
    public string FileFormat { get; private set; } = null!;

    /// <summary>文件大小（字节）。</summary>
    public long FileSizeBytes { get; private set; }

    /// <summary>文件 MD5。</summary>
    public string Md5 { get; private set; } = null!;

    /// <summary>模型文件在 BLOB 中的键名。</summary>
    public string BlobName { get; private set; } = null!;

    /// <summary>文件角色。</summary>
    public AiModelFileRole FileRole { get; private set; }

    /// <summary>MD5 校验是否通过。</summary>
    public bool Md5Verified { get; private set; }

    /// <summary>同一模型下的显示顺序。</summary>
    public int SortOrder { get; private set; }

    /// <summary>转换目标类型。</summary>
    public AiModelResolvedConversionType? ConversionTargetType { get; private set; }

    /// <summary>转换状态。</summary>
    public AiModelFileConversionStatus ConversionStatus { get; private set; }

    /// <summary>转换错误信息。</summary>
    public string? ConversionErrorMessage { get; private set; }

    /// <summary>转换完成时间。</summary>
    public DateTime? ConversionTime { get; private set; }

    /// <summary>EF Core 使用的无参构造函数。</summary>
    protected AiModelFile() { }

    /// <summary>
    /// 创建新的 AI 模型文件记录。
    /// </summary>
    public static AiModelFile Create(
        Guid id,
        Guid aiModelId,
        string originalFileName,
        string displayName,
        string fileFormat,
        long fileSizeBytes,
        string md5,
        string blobName,
        AiModelFileRole fileRole,
        bool md5Verified,
        int sortOrder
    )
    {
        return new AiModelFile
        {
            Id = id,
            AiModelId = aiModelId,
            IsOriginalFile = true,
            IsConvertedFile = false,
            SourceFileId = null,
            OriginalFileName = Check.NotNullOrWhiteSpace(
                originalFileName,
                nameof(originalFileName),
                AiModelConsts.MaxOriginalFileNameLength
            ),
            DisplayName = Check.NotNullOrWhiteSpace(
                displayName,
                nameof(displayName),
                AiModelConsts.MaxNameLength
            ),
            FileFormat = Check.NotNullOrWhiteSpace(
                fileFormat,
                nameof(fileFormat),
                AiModelConsts.MaxFileFormatLength
            ),
            FileSizeBytes = fileSizeBytes,
            Md5 = Check
                .NotNullOrWhiteSpace(md5, nameof(md5), AiModelConsts.MaxMd5Length)
                .Trim()
                .ToLowerInvariant(),
            BlobName = Check.NotNullOrWhiteSpace(
                blobName,
                nameof(blobName),
                AiModelConsts.MaxBlobNameLength
            ),
            FileRole = fileRole,
            Md5Verified = md5Verified,
            SortOrder = sortOrder,
            ConversionTargetType = null,
            ConversionStatus = AiModelFileConversionStatus.None,
            ConversionErrorMessage = null,
            ConversionTime = null,
        };
    }

    /// <summary>
    /// 创建新的转换产物文件记录。
    /// </summary>
    public static AiModelFile CreateConvertedProduct(
        Guid id,
        Guid aiModelId,
        Guid sourceFileId,
        string originalFileName,
        string displayName,
        string fileFormat,
        long fileSizeBytes,
        string md5,
        string blobName,
        AiModelFileRole fileRole,
        AiModelResolvedConversionType conversionTargetType,
        int sortOrder
    )
    {
        return new AiModelFile
        {
            Id = id,
            AiModelId = aiModelId,
            IsOriginalFile = false,
            IsConvertedFile = true,
            SourceFileId = sourceFileId,
            OriginalFileName = Check.NotNullOrWhiteSpace(
                originalFileName,
                nameof(originalFileName),
                AiModelConsts.MaxOriginalFileNameLength
            ),
            DisplayName = Check.NotNullOrWhiteSpace(
                displayName,
                nameof(displayName),
                AiModelConsts.MaxNameLength
            ),
            FileFormat = Check.NotNullOrWhiteSpace(
                fileFormat,
                nameof(fileFormat),
                AiModelConsts.MaxFileFormatLength
            ),
            FileSizeBytes = fileSizeBytes,
            Md5 = Check
                .NotNullOrWhiteSpace(md5, nameof(md5), AiModelConsts.MaxMd5Length)
                .Trim()
                .ToLowerInvariant(),
            BlobName = Check.NotNullOrWhiteSpace(
                blobName,
                nameof(blobName),
                AiModelConsts.MaxBlobNameLength
            ),
            FileRole = fileRole,
            Md5Verified = true,
            SortOrder = sortOrder,
            ConversionTargetType = conversionTargetType,
            ConversionStatus = AiModelFileConversionStatus.Completed,
            ConversionErrorMessage = null,
            ConversionTime = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// 更新文件角色。
    /// </summary>
    public void UpdateFileRole(AiModelFileRole fileRole)
    {
        FileRole = fileRole;
    }

    /// <summary>
    /// 更新文件排序。
    /// </summary>
    public void UpdateSortOrder(int sortOrder)
    {
        SortOrder = sortOrder;
    }

    /// <summary>
    /// 更新转换状态。
    /// </summary>
    public void UpdateConversionState(
        AiModelFileConversionStatus conversionStatus,
        AiModelResolvedConversionType? conversionTargetType = null,
        string? conversionErrorMessage = null,
        DateTime? conversionTime = null
    )
    {
        ConversionStatus = conversionStatus;
        if (conversionTargetType.HasValue)
        {
            ConversionTargetType = conversionTargetType.Value;
        }
        ConversionErrorMessage = string.IsNullOrWhiteSpace(conversionErrorMessage)
            ? null
            : Check.Length(
                conversionErrorMessage.Trim(),
                nameof(conversionErrorMessage),
                AiModelConsts.MaxErrorMessageLength
            );
        ConversionTime = conversionTime;
    }
}
