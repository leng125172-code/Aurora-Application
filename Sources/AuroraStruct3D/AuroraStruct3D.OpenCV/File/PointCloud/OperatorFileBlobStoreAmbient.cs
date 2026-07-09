namespace AuroraStruct3D.OpenCV.File.PointCloud;

/// <summary>
/// 工作流执行期间供算子访问的 operator-file BLOB 写入器。
/// </summary>
public interface IOperatorFileBlobStore
{
    /// <summary>
    /// 保存点云 PLY 文件到 BLOB，并返回保存后的 blob 键。
    /// </summary>
    /// <param name="fileName">目标文件名。</param>
    /// <param name="content">PLY 文件内容。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>blob 键。</returns>
    Task<string> SavePointCloudPlyAsync(
        string fileName,
        byte[] content,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 保存 PNG 图像到 BLOB，并返回保存后的 blob 键。
    /// </summary>
    /// <param name="fileName">目标文件名。</param>
    /// <param name="content">PNG 文件内容。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>blob 键。</returns>
    Task<string> SaveImagePngAsync(
        string fileName,
        byte[] content,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 根据 blob 键构建下载 URL。
    /// </summary>
    /// <param name="blobName">blob 键。</param>
    /// <returns>下载 URL。</returns>
    string BuildDownloadUrl(string blobName);

    /// <summary>
    /// 根据 blob 键构建预览 URL。
    /// </summary>
    /// <param name="blobName">blob 键。</param>
    /// <returns>预览 URL。</returns>
    string BuildPreviewUrl(string blobName);
}

/// <summary>
/// 基于 AsyncLocal 的执行期 operator-file BLOB 写入环境。
/// </summary>
public static class OperatorFileBlobStoreAmbient
{
    private static readonly AsyncLocal<IOperatorFileBlobStore?> CurrentStore = new();

    /// <summary>
    /// 获取当前执行期可用的 blob 写入器。
    /// </summary>
    public static IOperatorFileBlobStore Current =>
        CurrentStore.Value
        ?? throw new InvalidOperationException(
            "当前工作流执行上下文未配置 operator-file BLOB 写入器。"
        );

    /// <summary>
    /// 推入新的 blob 写入器作用域。
    /// </summary>
    /// <param name="store">写入器实例。</param>
    /// <returns>用于恢复上一个环境的作用域对象。</returns>
    public static IDisposable Push(IOperatorFileBlobStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        IOperatorFileBlobStore? previous = CurrentStore.Value;
        CurrentStore.Value = store;
        return new RestoreScope(previous);
    }

    private sealed class RestoreScope : IDisposable
    {
        private readonly IOperatorFileBlobStore? _previous;
        private bool _disposed;

        public RestoreScope(IOperatorFileBlobStore? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            CurrentStore.Value = _previous;
            _disposed = true;
        }
    }
}
