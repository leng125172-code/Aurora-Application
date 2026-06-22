namespace AuroraStruct3D.OperatorFile.Dtos;

/// <summary>
/// 上传算子文件结果 DTO。
/// </summary>
public class UploadOperatorFileResultDto
{
    /// <summary>上传是否成功。</summary>
    public bool Success { get; set; }

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
}