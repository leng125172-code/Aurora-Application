using Volo.Abp.Caching.StackExchangeRedis;
using Volo.Abp.Modularity;

namespace Lion.AbpPro.StackExchangeRedis;

[DependsOn(typeof(AbpCachingStackExchangeRedisModule))]
public class AbpProStackExchangeRedisModule : AbpModule { }
