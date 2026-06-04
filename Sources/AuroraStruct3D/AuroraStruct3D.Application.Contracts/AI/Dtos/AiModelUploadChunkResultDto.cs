namespace AuroraStruct3D.AI.Dtos;

/// <summary>
/// AI 模型分片上传进度 DTO。
/// </summary>
public class AiModelUploadChunkResultDto
{
    /// <summary>上传会话标识。</summary>
    public Guid SessionId { get; set; }

    /// <summary>已接收分片数。</summary>
    public int UploadedChunks { get; set; }

    /// <summary>分片总数。</summary>
    public int TotalChunks { get; set; }

    /// <summary>已接收字节数。</summary>
    public long UploadedBytes { get; set; }

    /// <summary>文件总字节数。</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>下一个期望分片序号。</summary>
    public int NextChunkIndex { get; set; }

    /// <summary>本次分片是否被服务端接收。</summary>
    public bool Accepted { get; set; }
}
