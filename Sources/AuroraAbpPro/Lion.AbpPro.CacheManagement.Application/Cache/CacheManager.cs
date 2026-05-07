using Lion.AbpPro.StackExchangeRedis;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Volo.Abp;
using Volo.Abp.Caching;
using Volo.Abp.DependencyInjection;

namespace Lion.AbpPro.CacheManagement.Cache;

public class CacheManager : CacheManagementDomainService, ICacheManager, ISingletonDependency
{
    private readonly IStackExchangeRedisCacheManager _stackExchangeRedisCacheManager;
    private readonly RedisCacheOptions _redisCacheOptions;
    private readonly AbpDistributedCacheOptions _cacheOptions;

    public CacheManager(
        IStackExchangeRedisCacheManager stackExchangeRedisCacheManager,
        IOptions<RedisCacheOptions> redisCacheOptions,
        IOptions<AbpDistributedCacheOptions> cacheOptions
    )
    {
        _stackExchangeRedisCacheManager = stackExchangeRedisCacheManager;
        _cacheOptions = cacheOptions.Value;
        _redisCacheOptions = redisCacheOptions.Value;
    }

    public virtual async Task<IEnumerable<string>> GetKeysAsync(
        string key,
        CancellationToken cancellationToken = default
    )
    {
        var cache = await _stackExchangeRedisCacheManager.ConnectAsync(cancellationToken);

        // 缓存键名规则: InstanceName + (t + TenantId)(CurrentTenant.IsAvailable) + CacheItemName + KeyPrefix + Key
        // 缓存键名规则: InstanceName + (c:)(!CurrentTenant.IsAvailable) + CacheItemName + KeyPrefix + Key

        var match = "*";
        // abp*
        if (!_redisCacheOptions.InstanceName.IsNullOrWhiteSpace())
        {
            match = _redisCacheOptions.InstanceName;
        }

        // abp*t:xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx*
        // abp*c:*
        if (CurrentTenant.IsAvailable)
        {
            match += "t:" + CurrentTenant.Id.ToString() + "*";
        }
        else
        {
            match += "c:*";
        }

        // abp*t:xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx*application*
        // abp*c:application*
        if (!_cacheOptions.KeyPrefix.IsNullOrWhiteSpace())
        {
            match += _cacheOptions.KeyPrefix.EnsureEndsWith('*');
        }
        if (key.IsNotNullOrWhiteSpace())
        {
            match = $"*{key.Trim()}*";
        }
        var args = new object[] { "0", "match", match, "count", 50000 };

        var result = await cache.ExecuteAsync("scan", args);

        var results = (RedisResult[])result;
        return (string[])results?.LastOrDefault();
    }

    public virtual async Task<CacheValueResult> GetValueAsync(
        string key,
        CancellationToken cancellationToken = default
    )
    {
        long size;
        var values = new Dictionary<string, object>();
        var cache = await _stackExchangeRedisCacheManager.ConnectAsync(cancellationToken);
        // type RedisKey
        var type = await cache.KeyTypeAsync(key);
        // ttl RedisKey
        var ttl = await cache.KeyTimeToLiveAsync(key);

        switch (type)
        {
            case RedisType.Hash:
                size = await cache.HashLengthAsync(key);
                await foreach (
                    var hvalue in cache.HashScanAsync(key).WithCancellation(cancellationToken)
                )
                {
                    if (!hvalue.Name.IsNullOrEmpty)
                    {
                        values.Add(
                            hvalue.Name.ToString(),
                            hvalue.Value.IsNullOrEmpty ? "" : hvalue.Value.ToString()
                        );
                    }
                }

                break;
            case RedisType.String:
                size = await cache.StringLengthAsync(key);
                var svalue = await cache.StringGetAsync(key);
                values.Add("value", svalue.IsNullOrEmpty ? "" : svalue.ToString());
                break;
            case RedisType.List:
                size = await cache.ListLengthAsync(key);
                var lvalues = await cache.ListRangeAsync(key);
                for (var lindex = 0; lindex < lvalues.Length; lindex++)
                {
                    if (!lvalues[lindex].IsNullOrEmpty)
                    {
                        values.Add(
                            $"index.{lindex}",
                            lvalues[lindex].IsNullOrEmpty ? "" : lvalues[lindex].ToString()
                        );
                    }
                }

                break;
            default:
                throw new BusinessException();
        }

        return new CacheValueResult(
            type.ToString(),
            size,
            values,
            ttl.HasValue ? (long)ttl.Value.TotalSeconds : 0
        );
    }

    public virtual async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var cacheKey = key;
        if (
            !_redisCacheOptions.InstanceName.IsNullOrWhiteSpace()
            && cacheKey.StartsWith(_redisCacheOptions.InstanceName)
        )
        {
            cacheKey = cacheKey.Substring(_redisCacheOptions.InstanceName.Length);
        }

        await _stackExchangeRedisCacheManager.RedisCache.RemoveAsync(cacheKey, cancellationToken);
    }
}
