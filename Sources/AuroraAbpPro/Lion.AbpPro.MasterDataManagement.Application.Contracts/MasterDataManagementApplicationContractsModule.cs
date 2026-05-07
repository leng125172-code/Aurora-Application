namespace Lion.AbpPro.MasterDataManagement
{
    [DependsOn(
        typeof(MasterDataManagementDomainSharedModule),
        typeof(AbpDddApplicationContractsModule),
        typeof(AbpAuthorizationModule)
    )]
    public class MasterDataManagementApplicationContractsModule : AbpModule { }
}
