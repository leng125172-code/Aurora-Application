namespace Lion.AbpPro.CodeManagement
{
    [DependsOn(
        typeof(CodeManagementDomainModule),
        typeof(CodeManagementApplicationContractsModule),
        typeof(AbpDddApplicationModule)
    )]
    public class CodeManagementApplicationModule : AbpModule { }
}
