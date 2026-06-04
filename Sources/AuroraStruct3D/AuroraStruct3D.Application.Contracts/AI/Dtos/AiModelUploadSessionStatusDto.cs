namespace AuroraStruct3D.AI.Dtos;

/// <summary>
/// AI 模型上传会话状态 DTO。
/// </summary>
public class AiModelUploadSessionStatusDto
{
    /// <summary>上传会话标识。</summary>
    public Guid SessionId { get; set; }

    /// <summary>原始文件名。</summary>
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>文件 MD5。</summary>
    public string Md5 { get; set; } = string.Empty;

    /// <summary>文件总字节数。</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>分片大小（字节）。</summary>
    public int ChunkSizeBytes { get; set; }

    /// <summary>分片总数。</summary>
    public int TotalChunks { get; set; }

    /// <summary>已上传分片数。</summary>
    public int UploadedChunks { get; set; }

    /// <summary>已上传字节数。</summary>
    public long UploadedBytes { get; set; }

    /// <summary>下一个期望分片序号。</summary>
    public int NextChunkIndex { get; set; }

    /// <summary>是否已完成全部分片上传。</summary>
    public bool IsCompleted { get; set; }

    /// <summary>最后更新时间（UTC）。</summary>
    public DateTime LastUpdatedTime { get; set; }
}
