namespace AuroraStruct3D.AI.Dtos;

/// <summary>
/// 初始化 AI 模型分片上传结果 DTO。
/// </summary>
public class InitializeAiModelUploadResultDto
{
    /// <summary>上传会话标识。</summary>
    public Guid SessionId { get; set; }

    /// <summary>原始文件名。</summary>
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>文件大小（字节）。</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>分片大小（字节）。</summary>
    public int ChunkSizeBytes { get; set; }

    /// <summary>分片总数。</summary>
    public int TotalChunks { get; set; }

    /// <summary>已接收分片数。</summary>
    public int UploadedChunks { get; set; }

    /// <summary>已接收字节数。</summary>
    public long UploadedBytes { get; set; }
}
