using StackExchange.Redis;
using Volo.Abp.Caching.StackExchangeRedis;

namespace Lion.AbpPro.StackExchangeRedis;

public interface IStackExchangeRedisCacheManager
{
    /// <summary>
    /// 获取当前分布式缓存的 Redis 实现实例。
    /// </summary>
    AbpRedisCache RedisCache { get; }

    /// <summary>
    /// 异步连接到 Redis 数据库。
    /// </summary>
    /// <param name="token">取消操作的通知。</param>
    /// <returns>表示异步操作的 ValueTask，其结果为 Redis 数据库实例。</returns>
    ValueTask<IDatabase> ConnectAsync(CancellationToken token = default);
}
