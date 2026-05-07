namespace Lion.AbpPro.CodeManagement
{
    [DependsOn(
        typeof(CodeManagementDomainSharedModule),
        typeof(AbpDddApplicationContractsModule),
        typeof(AbpAuthorizationModule)
    )]
    public class CodeManagementApplicationContractsModule : AbpModule { }
}
