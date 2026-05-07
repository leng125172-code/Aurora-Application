namespace Lion.AbpPro.CacheManagement
{
    [DependsOn(
        typeof(CacheManagementDomainSharedModule),
        typeof(AbpDddApplicationContractsModule),
        typeof(AbpAuthorizationModule)
    )]
    public class CacheManagementApplicationContractsModule : AbpModule { }
}
