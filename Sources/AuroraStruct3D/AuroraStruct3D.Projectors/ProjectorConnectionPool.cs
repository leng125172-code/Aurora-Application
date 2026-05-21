using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AuroraStruct3D.Projectors;

/// <summary>
/// 投影机连接池实现。
/// 内部使用 <see cref="ConcurrentDictionary{TKey,TValue}"/> 维护每台设备的独立连接实例，
/// 解决单例 IDlpProjectorService 无法管理多设备的问题。
/// </summary>
public sealed class ProjectorConnectionPool : IProjectorConnectionPool, IDisposable
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentDictionary<Guid, DlpProjectorService> _pool = new();
    private bool _disposed;

    /// <summary>
    /// 构造投影机连接池
    /// </summary>
    /// <param name="serviceScopeFactory">用于操作日志写入的 DI 作用域工厂</param>
    /// <param name="loggerFactory">日志工厂（每个实例独立 Logger）</param>
    public ProjectorConnectionPool(
        IServiceScopeFactory serviceScopeFactory,
        ILoggerFactory loggerFactory
    )
    {
        _serviceScopeFactory = serviceScopeFactory;
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc/>
    public IDlpProjectorService GetOrCreate(Guid deviceId)
    {
        return _pool.GetOrAdd(
            deviceId,
            id =>
            {
                ILogger<DlpProjectorService> logger =
                    _loggerFactory.CreateLogger<DlpProjectorService>();
                DlpProjectorService svc = new(logger, _serviceScopeFactory);
                svc.SetProjectorDeviceId(id);
                return svc;
            }
        );
    }

    /// <inheritdoc/>
    public IDlpProjectorService? TryGet(Guid deviceId)
    {
        return _pool.TryGetValue(deviceId, out DlpProjectorService? svc) ? svc : null;
    }

    /// <inheritdoc/>
    public async Task RemoveAsync(Guid deviceId)
    {
        if (_pool.TryRemove(deviceId, out DlpProjectorService? svc))
        {
            try
            {
                await svc.DisconnectAsync().ConfigureAwait(false);
            }
            catch
            {
                // 断开失败时忽略，继续清理
            }
            finally
            {
                svc.Dispose();
            }
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<Guid> GetAllDeviceIds() => _pool.Keys.ToList();

    /// <summary>释放连接池中所有设备连接</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        foreach (KeyValuePair<Guid, DlpProjectorService> kv in _pool)
        {
            try
            {
                kv.Value.Dispose();
            }
            catch
            {
                // 忽略释放时的异常
            }
        }
        _pool.Clear();
    }
}
