namespace Lion.AbpPro.CacheManagement.Cache;

public interface ICacheManager
{
    /// <summary>
    /// 获取redis所有key
    /// </summary>
    Task<IEnumerable<string>> GetKeysAsync(
        string key,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 通过key获取缓存value
    /// </summary>
    Task<CacheValueResult> GetValueAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 通过key删除缓存
    /// </summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
