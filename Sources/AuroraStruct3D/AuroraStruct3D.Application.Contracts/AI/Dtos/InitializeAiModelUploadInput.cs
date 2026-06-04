using System.ComponentModel.DataAnnotations;

namespace AuroraStruct3D.AI.Dtos;

/// <summary>
/// 初始化 AI 模型分片上传输入 DTO。
/// </summary>
public class InitializeAiModelUploadInput
{
    /// <summary>原始文件名。</summary>
    [Required]
    [MaxLength(512)]
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>文件大小（字节）。</summary>
    [Range(1, long.MaxValue)]
    public long FileSizeBytes { get; set; }

    /// <summary>分片大小（字节）。</summary>
    [Range(1, 1024 * 1024 * 1024)]
    public int ChunkSizeBytes { get; set; }

    /// <summary>分片总数。</summary>
    [Range(1, int.MaxValue)]
    public int TotalChunks { get; set; }

    /// <summary>文件 MD5。</summary>
    [Required]
    [MaxLength(AiModelConsts.MaxMd5Length)]
    public string Md5 { get; set; } = string.Empty;

    /// <summary>文件类型。</summary>
    public AiModelFileRole FileRole { get; set; } = AiModelFileRole.SingleWholeModel;

    /// <summary>显示顺序。</summary>
    public int SortOrder { get; set; }
}
