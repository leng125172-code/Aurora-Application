namespace Lion.AbpPro.DynamicMenuManagement
{
    [DependsOn(
        typeof(DynamicMenuManagementDomainSharedModule),
        typeof(AbpDddApplicationContractsModule),
        typeof(AbpAuthorizationModule)
    )]
    public class DynamicMenuManagementApplicationContractsModule : AbpModule { }
}
