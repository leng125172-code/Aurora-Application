namespace Lion.AbpPro.CacheManagement
{
    [DependsOn(typeof(AbpDddDomainModule), typeof(CacheManagementDomainSharedModule))]
    public class CacheManagementDomainModule : AbpModule { }
}
