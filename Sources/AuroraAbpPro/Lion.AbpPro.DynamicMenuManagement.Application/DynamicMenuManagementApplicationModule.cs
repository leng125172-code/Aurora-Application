namespace Lion.AbpPro.DynamicMenuManagement
{
    [DependsOn(
        typeof(DynamicMenuManagementDomainModule),
        typeof(DynamicMenuManagementApplicationContractsModule),
        typeof(AbpDddApplicationModule)
    )]
    public class DynamicMenuManagementApplicationModule : AbpModule { }
}
