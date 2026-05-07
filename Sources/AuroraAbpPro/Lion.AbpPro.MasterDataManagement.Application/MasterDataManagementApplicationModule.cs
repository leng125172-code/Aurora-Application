namespace Lion.AbpPro.MasterDataManagement
{
    [DependsOn(
        typeof(MasterDataManagementDomainModule),
        typeof(MasterDataManagementApplicationContractsModule),
        typeof(AbpDddApplicationModule)
    )]
    public class MasterDataManagementApplicationModule : AbpModule { }
}
