namespace Lion.AbpPro.MasterDataManagement
{
    [DependsOn(
        typeof(AbpDddDomainModule),
        typeof(MasterDataManagementDomainSharedModule),
        typeof(AbpCachingModule)
    )]
    public class MasterDataManagementDomainModule : AbpModule { }
}
