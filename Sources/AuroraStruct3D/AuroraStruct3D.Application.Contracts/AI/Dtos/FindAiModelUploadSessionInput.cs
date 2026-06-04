using System.ComponentModel.DataAnnotations;

namespace AuroraStruct3D.AI.Dtos;

/// <summary>
/// 按文件特征查找 AI 模型上传会话输入 DTO。
/// </summary>
public class FindAiModelUploadSessionInput
{
    /// <summary>原始文件名。</summary>
    [Required]
    [MaxLength(512)]
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>文件 MD5。</summary>
    [Required]
    [MaxLength(AiModelConsts.MaxMd5Length)]
    public string Md5 { get; set; } = string.Empty;

    /// <summary>文件总大小（字节）。</summary>
    [Range(1, long.MaxValue)]
    public long FileSizeBytes { get; set; }

    /// <summary>分片大小（字节）。</summary>
    public int? ChunkSizeBytes { get; set; }

    /// <summary>分片总数。</summary>
    public int? TotalChunks { get; set; }
}
