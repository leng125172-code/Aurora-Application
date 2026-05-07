namespace Lion.AbpPro.TemplateManagement
{
    [DependsOn(
        typeof(AbpDddDomainModule),
        typeof(TemplateManagementDomainSharedModule),
        typeof(AbpCachingModule)
    )]
    public class TemplateManagementDomainModule : AbpModule { }
}
