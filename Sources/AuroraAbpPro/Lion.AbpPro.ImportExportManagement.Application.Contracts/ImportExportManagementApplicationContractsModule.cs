namespace Lion.AbpPro.ImportExportManagement
{
    [DependsOn(
        typeof(ImportExportManagementDomainSharedModule),
        typeof(AbpDddApplicationContractsModule),
        typeof(AbpAuthorizationModule)
    )]
    public class ImportExportManagementApplicationContractsModule : AbpModule { }
}
