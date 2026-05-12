using Lion.AbpPro.StackExchangeRedis;

namespace Lion.AbpPro.CacheManagement
{
    [DependsOn(
        typeof(CacheManagementDomainModule),
        typeof(CacheManagementApplicationContractsModule),
        typeof(AbpDddApplicationModule),
        typeof(AbpProStackExchangeRedisModule)
    )]
    public class CacheManagementApplicationModule : AbpModule { }
}
