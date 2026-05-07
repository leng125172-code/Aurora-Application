using Lion.AbpPro.FileManagement;

namespace Lion.AbpPro.ImportExportManagement
{
    [DependsOn(
        typeof(ImportExportManagementDomainModule),
        typeof(ImportExportManagementApplicationContractsModule),
        typeof(AbpDddApplicationModule),
        typeof(FileManagementDomainModule)
    )]
    public class ImportExportManagementApplicationModule : AbpModule { }
}
