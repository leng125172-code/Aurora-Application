namespace AuroraStruct3D.OpenCV.File.PointCloud;

public interface IProductModelPointCloudStore
{
    Task<string> MaterializeAsync(Guid modelId);
}

public static class ProductModelStoreAmbient
{
    private static readonly AsyncLocal<IProductModelPointCloudStore?> CurrentStore = new();

    public static IProductModelPointCloudStore Current =>
        CurrentStore.Value
        ?? throw new InvalidOperationException("当前工作流未配置产品模型库访问服务。");

    public static IDisposable Push(IProductModelPointCloudStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        IProductModelPointCloudStore? previous = CurrentStore.Value;
        CurrentStore.Value = store;
        return new RestoreScope(previous);
    }

    private sealed class RestoreScope(IProductModelPointCloudStore? previous) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            CurrentStore.Value = previous;
        }
    }
}
