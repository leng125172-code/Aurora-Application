using Volo.Abp.Domain.Entities;

namespace AuroraStruct3D.OperatorFile;

/// <summary>
/// 算子文件上传记录聚合根。
/// 记录每次上传文件的元数据、过期时间和使用状态，用于过期文件清理。
/// <para>数据库表：AbpProOperatorFileRecords</para>
/// </summary>
public class OperatorFileRecord : AggregateRoot<Guid>
{
    /// <summary>所属项目唯一标识。</summary>
    public Guid ProjectId { get; set; }

    /// <summary>算子唯一标识。</summary>
    public Guid OperatorId { get; set; }

    /// <summary>算子显示名称。</summary>
    public string OperatorDisplayName { get; set; } = string.Empty;

    /// <summary>原始文件名。</summary>
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>BLOB 存储键名。</summary>
    public string BlobName { get; set; } = string.Empty;

    /// <summary>预览图 BLOB 名称列表，以逗号分隔。</summary>
    public string PreviewBlobNames { get; set; } = string.Empty;

    /// <summary>文件大小（字节）。</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>文件 MD5 校验值。</summary>
    public string Md5 { get; set; } = string.Empty;

    /// <summary>过期时间。超过此时间且未被使用，文件将被清理。</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>是否已使用（已关联到工作流）。</summary>
    public bool IsUsed { get; set; }

    /// <summary>创建时间。</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 确认文件已使用，取消过期时间。
    /// </summary>
    public void MarkAsUsed()
    {
        IsUsed = true;
        ExpiresAt = null;
    }

    /// <summary>
    /// 获取所有相关的 BLOB 名称（原文件 + 预览图），用于清理时删除。
    /// </summary>
    public List<string> GetAllBlobNames()
    {
        var names = new List<string> { BlobName };

        if (!string.IsNullOrWhiteSpace(PreviewBlobNames))
        {
            names.AddRange(
                PreviewBlobNames.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
            );
        }

        return names;
    }
}
