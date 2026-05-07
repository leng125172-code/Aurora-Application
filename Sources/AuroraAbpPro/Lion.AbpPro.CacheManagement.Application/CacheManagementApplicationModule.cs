namespace Lion.AbpPro.CacheManagement
{
    [DependsOn(
        typeof(CacheManagementDomainModule),
        typeof(CacheManagementApplicationContractsModule),
        typeof(AbpDddApplicationModule)
    )]
    public class CacheManagementApplicationModule : AbpModule { }
}
