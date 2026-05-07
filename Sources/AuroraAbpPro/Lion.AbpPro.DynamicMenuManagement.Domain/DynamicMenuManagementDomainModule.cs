namespace Lion.AbpPro.DynamicMenuManagement
{
    [DependsOn(
        typeof(AbpDddDomainModule),
        typeof(DynamicMenuManagementDomainSharedModule),
        typeof(AbpCachingModule)
    )]
    public class DynamicMenuManagementDomainModule : AbpModule { }
}
