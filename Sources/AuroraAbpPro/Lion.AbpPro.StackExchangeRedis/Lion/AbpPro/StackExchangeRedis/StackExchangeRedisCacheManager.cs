using System.Reflection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using StackExchange.Redis;
using Volo.Abp;
using Volo.Abp.Caching.StackExchangeRedis;
using Volo.Abp.DependencyInjection;

namespace Lion.AbpPro.StackExchangeRedis;

/// <summary>
/// 提供对 StackExchange Redis 的连接管理功能。
/// 该类通过反射调用 AbpRedisCache 的非公开 ConnectAsync 方法实现 Redis 数据库连接。
/// </summary>
public class StackExchangeRedisCacheManager : ISingletonDependency, IStackExchangeRedisCacheManager
{
    /// <summary>
    /// 缓存 AbpRedisCache 类型中的 ConnectAsync 方法信息，用于异步连接 Redis 数据库。
    /// </summary>
    private readonly MethodInfo _connectAsyncMethod;

    /// <summary>
    /// 分布式缓存实例，用于访问 Redis 缓存服务。
    /// </summary>
    private readonly IDistributedCache _distributedCache;

    /// <summary>
    /// 获取当前分布式缓存的 Redis 实现实例。
    /// </summary>
    public AbpRedisCache RedisCache => _distributedCache.As<AbpRedisCache>();

    /// <summary>
    /// 标记当前缓存实现是否为 Redis。
    /// </summary>
    private readonly bool _isRedisEnabled;

    /// <summary>
    /// 初始化 StackExchangeRedisCacheManager 类的新实例。
    /// </summary>
    /// <param name="distributedCache">分布式缓存服务实例。</param>
    /// <exception cref="InvalidOperationException">当无法找到 ConnectAsync 方法时抛出异常。</exception>
    public StackExchangeRedisCacheManager(IDistributedCache distributedCache)
    {
        _distributedCache = distributedCache;
        _isRedisEnabled = distributedCache is RedisCache;

        if (_isRedisEnabled)
        {
            var type = typeof(AbpRedisCache);
            _connectAsyncMethod = typeof(AbpRedisCache).GetMethod(
                "ConnectAsync",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            if (_connectAsyncMethod == null)
            {
                throw new InvalidOperationException(
                    $"Method 'ConnectAsync' not found on type '{type.FullName}'."
                );
            }
        }
    }

    /// <summary>
    /// 异步连接到 Redis 数据库。
    /// </summary>
    /// <param name="token">取消操作的通知。</param>
    /// <returns>表示异步操作的 ValueTask，其结果为 Redis 数据库实例。</returns>
    public virtual ValueTask<IDatabase> ConnectAsync(CancellationToken token = default)
    {
        if (!_isRedisEnabled)
        {
            throw new UserFriendlyException("当前未启用 Redis 缓存，缓存管理功能不可用。");
        }

        return (ValueTask<IDatabase>)_connectAsyncMethod.Invoke(RedisCache, [token])!;
    }
}
