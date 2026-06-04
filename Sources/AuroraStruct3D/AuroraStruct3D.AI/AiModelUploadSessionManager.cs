using System.Collections.Concurrent;
using Volo.Abp;

namespace AuroraStruct3D.AI;

/// <summary>
/// 基于本地临时文件的 AI 模型分片上传会话管理器。
/// </summary>
public sealed class AiModelUploadSessionManager : IAiModelUploadSessionManager
{
    private static readonly TimeSpan SessionRetention = TimeSpan.FromHours(24);
    private readonly ConcurrentDictionary<Guid, UploadSessionState> _sessions = new();

    /// <inheritdoc/>
    public Task<AiModelUploadSessionSnapshot> CreateAsync(
        string originalFileName,
        string md5,
        long fileSizeBytes,
        int chunkSizeBytes,
        int totalChunks
    )
    {
        CleanExpiredSessions();

        int expectedTotalChunks = checked(
            (int)Math.Ceiling(fileSizeBytes / (double)chunkSizeBytes)
        );
        if (expectedTotalChunks != totalChunks)
        {
            throw new UserFriendlyException("分片总数与文件大小不匹配。");
        }

        Guid sessionId = Guid.NewGuid();
        string tempFilePath = Path.Combine(
            Path.GetTempPath(),
            $"ai-model-session-{sessionId:N}.upload"
        );
        using (File.Create(tempFilePath)) { }

        UploadSessionState state = new(
            sessionId,
            originalFileName,
            md5,
            tempFilePath,
            fileSizeBytes,
            chunkSizeBytes,
            totalChunks
        );
        if (!_sessions.TryAdd(sessionId, state))
        {
            DeleteFileQuietly(tempFilePath);
            throw new UserFriendlyException("创建上传会话失败，请重试。");
        }

        return Task.FromResult(state.ToSnapshot());
    }

    /// <inheritdoc/>
    public Task<AiModelUploadSessionSnapshot> GetAsync(Guid sessionId)
    {
        UploadSessionState state = GetRequiredState(sessionId);
        return Task.FromResult(state.ToSnapshot());
    }

    /// <inheritdoc/>
    public Task<AiModelUploadSessionSnapshot?> FindAsync(
        string originalFileName,
        string md5,
        long fileSizeBytes,
        int? chunkSizeBytes = null,
        int? totalChunks = null
    )
    {
        CleanExpiredSessions();

        string normalizedFileName = Path.GetFileName(originalFileName.Trim());
        string normalizedMd5 = md5.Trim().ToLowerInvariant();

        AiModelUploadSessionSnapshot? session = _sessions
            .Values.Where(state =>
                string.Equals(
                    state.OriginalFileName,
                    normalizedFileName,
                    StringComparison.OrdinalIgnoreCase
                )
                && string.Equals(state.Md5, normalizedMd5, StringComparison.OrdinalIgnoreCase)
                && state.FileSizeBytes == fileSizeBytes
                && (!chunkSizeBytes.HasValue || state.ChunkSizeBytes == chunkSizeBytes.Value)
                && (!totalChunks.HasValue || state.TotalChunks == totalChunks.Value)
            )
            .OrderByDescending(state => state.LastUpdatedTime)
            .Select(state => state.ToSnapshot())
            .FirstOrDefault();

        return Task.FromResult(session);
    }

    /// <inheritdoc/>
    public async Task<AiModelUploadAppendResult> AppendChunkAsync(
        Guid sessionId,
        int chunkIndex,
        Stream source,
        CancellationToken cancellationToken = default
    )
    {
        UploadSessionState state = GetRequiredState(sessionId);
        await state.Gate.WaitAsync(cancellationToken);
        try
        {
            if (chunkIndex < state.UploadedChunks)
            {
                return new AiModelUploadAppendResult
                {
                    Session = state.ToSnapshot(),
                    Accepted = false,
                };
            }

            if (chunkIndex != state.UploadedChunks)
            {
                throw new UserFriendlyException(
                    $"分片顺序不正确，当前期望分片序号为 {state.UploadedChunks}。"
                );
            }

            if (state.UploadedChunks >= state.TotalChunks)
            {
                return new AiModelUploadAppendResult
                {
                    Session = state.ToSnapshot(),
                    Accepted = false,
                };
            }

            long originalLength = state.UploadedBytes;
            long appendedBytes = 0;
            byte[] buffer = new byte[1024 * 1024];

            await using FileStream target = new(
                state.TempFilePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan
            );

            while (true)
            {
                int read = await source.ReadAsync(
                    buffer.AsMemory(0, buffer.Length),
                    cancellationToken
                );
                if (read == 0)
                {
                    break;
                }

                appendedBytes += read;
                if (originalLength + appendedBytes > state.FileSizeBytes)
                {
                    target.SetLength(originalLength);
                    throw new UserFriendlyException("上传分片超出文件总大小，请重新上传。");
                }

                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            state.UploadedBytes = originalLength + appendedBytes;
            state.UploadedChunks++;
            state.LastUpdatedTime = DateTime.UtcNow;

            return new AiModelUploadAppendResult { Session = state.ToSnapshot(), Accepted = true };
        }
        finally
        {
            state.Gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task RemoveAsync(Guid sessionId)
    {
        if (!_sessions.TryRemove(sessionId, out UploadSessionState? state))
        {
            return;
        }

        await state.Gate.WaitAsync();
        try
        {
            DeleteFileQuietly(state.TempFilePath);
        }
        finally
        {
            state.Gate.Release();
            state.Gate.Dispose();
        }
    }

    private UploadSessionState GetRequiredState(Guid sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out UploadSessionState? state))
        {
            return state;
        }

        throw new UserFriendlyException("上传会话不存在或已过期，请重新选择文件上传。");
    }

    private void CleanExpiredSessions()
    {
        DateTime now = DateTime.UtcNow;
        List<Guid> expiredIds = _sessions
            .Where(item => now - item.Value.LastUpdatedTime > SessionRetention)
            .Select(item => item.Key)
            .ToList();

        foreach (Guid sessionId in expiredIds)
        {
            _ = RemoveAsync(sessionId);
        }
    }

    private static void DeleteFileQuietly(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return;
        }

        try
        {
            File.Delete(filePath);
        }
        catch { }
    }

    private sealed class UploadSessionState
    {
        public UploadSessionState(
            Guid sessionId,
            string originalFileName,
            string md5,
            string tempFilePath,
            long fileSizeBytes,
            int chunkSizeBytes,
            int totalChunks
        )
        {
            SessionId = sessionId;
            OriginalFileName = Path.GetFileName(originalFileName.Trim());
            Md5 = md5.Trim().ToLowerInvariant();
            TempFilePath = tempFilePath;
            FileSizeBytes = fileSizeBytes;
            ChunkSizeBytes = chunkSizeBytes;
            TotalChunks = totalChunks;
            LastUpdatedTime = DateTime.UtcNow;
        }

        public Guid SessionId { get; }

        public string OriginalFileName { get; }

        public string Md5 { get; }

        public string TempFilePath { get; }

        public long FileSizeBytes { get; }

        public int ChunkSizeBytes { get; }

        public int TotalChunks { get; }

        public int UploadedChunks { get; set; }

        public long UploadedBytes { get; set; }

        public DateTime LastUpdatedTime { get; set; }

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public AiModelUploadSessionSnapshot ToSnapshot()
        {
            return new AiModelUploadSessionSnapshot
            {
                SessionId = SessionId,
                OriginalFileName = OriginalFileName,
                Md5 = Md5,
                TempFilePath = TempFilePath,
                FileSizeBytes = FileSizeBytes,
                ChunkSizeBytes = ChunkSizeBytes,
                TotalChunks = TotalChunks,
                UploadedChunks = UploadedChunks,
                UploadedBytes = UploadedBytes,
                LastUpdatedTime = LastUpdatedTime,
            };
        }
    }
}
