namespace AuroraStruct3D.AI;

/// <summary>
/// AI 模型分片上传会话管理器。
/// </summary>
public interface IAiModelUploadSessionManager
{
    /// <summary>
    /// 创建上传会话。
    /// </summary>
    Task<AiModelUploadSessionSnapshot> CreateAsync(
        string originalFileName,
        string md5,
        long fileSizeBytes,
        int chunkSizeBytes,
        int totalChunks
    );

    /// <summary>
    /// 获取上传会话快照。
    /// </summary>
    Task<AiModelUploadSessionSnapshot> GetAsync(Guid sessionId);

    /// <summary>
    /// 按文件特征查找上传会话。
    /// </summary>
    Task<AiModelUploadSessionSnapshot?> FindAsync(
        string originalFileName,
        string md5,
        long fileSizeBytes,
        int? chunkSizeBytes = null,
        int? totalChunks = null
    );

    /// <summary>
    /// 追加上传分片。
    /// </summary>
    Task<AiModelUploadAppendResult> AppendChunkAsync(
        Guid sessionId,
        int chunkIndex,
        Stream source,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 删除上传会话。
    /// </summary>
    Task RemoveAsync(Guid sessionId);
}

/// <summary>
/// AI 模型上传会话快照。
/// </summary>
public sealed class AiModelUploadSessionSnapshot
{
    /// <summary>会话标识。</summary>
    public required Guid SessionId { get; init; }

    /// <summary>原始文件名。</summary>
    public required string OriginalFileName { get; init; }

    /// <summary>文件 MD5。</summary>
    public required string Md5 { get; init; }

    /// <summary>临时文件路径。</summary>
    public required string TempFilePath { get; init; }

    /// <summary>文件总字节数。</summary>
    public required long FileSizeBytes { get; init; }

    /// <summary>分片大小（字节）。</summary>
    public required int ChunkSizeBytes { get; init; }

    /// <summary>分片总数。</summary>
    public required int TotalChunks { get; init; }

    /// <summary>已上传分片数。</summary>
    public required int UploadedChunks { get; init; }

    /// <summary>已上传字节数。</summary>
    public required long UploadedBytes { get; init; }

    /// <summary>最后更新时间。</summary>
    public required DateTime LastUpdatedTime { get; init; }
}

/// <summary>
/// AI 模型分片追加结果。
/// </summary>
public sealed class AiModelUploadAppendResult
{
    /// <summary>会话快照。</summary>
    public required AiModelUploadSessionSnapshot Session { get; init; }

    /// <summary>本次分片是否被接收。</summary>
    public required bool Accepted { get; init; }
}
