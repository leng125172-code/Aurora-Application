namespace Lion.AbpPro.TemplateManagement
{
    [DependsOn(
        typeof(TemplateManagementDomainSharedModule),
        typeof(AbpDddApplicationContractsModule),
        typeof(AbpAuthorizationModule)
    )]
    public class TemplateManagementApplicationContractsModule : AbpModule { }
}
