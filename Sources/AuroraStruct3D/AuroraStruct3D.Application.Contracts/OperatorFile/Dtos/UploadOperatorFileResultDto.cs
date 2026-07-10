namespace AuroraStruct3D.OperatorFile.Dtos;

/// <summary>
/// 上传算子文件结果 DTO。
/// <para>
/// 图片上传：<see cref="PreviewImages"/> 包含 1 个灰度预览图 Blob 名称。
/// 点云上传：<see cref="PreviewImages"/> 包含 3 个正交投影预览图 Blob 名称（XY、XZ、YZ）。
/// </para>
/// <para>
/// 文件有效期：上传后若未在 <see cref="ExpiresAt"/> 之前通过 <c>ConfirmAsync</c> 确认使用，
/// 文件将被定时清理任务自动删除。
/// </para>
/// </summary>
public class UploadOperatorFileResultDto
{
    /// <summary>上传是否成功。</summary>
    public bool Success { get; set; }

    /// <summary>所属项目唯一标识。</summary>
    public Guid ProjectId { get; set; }

    /// <summary>算子唯一标识。</summary>
    public Guid OperatorId { get; set; }

    /// <summary>算子显示名称。</summary>
    public string OperatorDisplayName { get; set; } = string.Empty;

    /// <summary>原始文件名。</summary>
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>保存后的 Blob 名称，可用于后续算子执行时引用。</summary>
    public string BlobName { get; set; } = string.Empty;

    /// <summary>文件大小（字节）。</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>文件 MD5 校验值。</summary>
    public string Md5 { get; set; } = string.Empty;

    /// <summary>文件过期时间。超过此时间未被确认使用，将被自动清理。</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>是否已被确认使用。</summary>
    public bool IsUsed { get; set; }

    /// <summary>
    /// 预览图下载 URL 列表。
    /// <list type="bullet">
    ///   <item>图片：1 个灰度预览图 BlobName</item>
    ///   <item>点云：3 个正交投影预览图 BlobName（XY、XZ、YZ）</item>
    /// </list>
    /// </summary>
    public List<PreviewImageDto> PreviewImages { get; set; } = new();
}

/// <summary>
/// 预览图信息 DTO。
/// </summary>
public class PreviewImageDto
{
    /// <summary>预览图名称标识，如 "灰度图"、"XY"、"XZ"、"YZ"。</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>预览图 Blob 名称。</summary>
    public string BlobName { get; set; } = string.Empty;
}
