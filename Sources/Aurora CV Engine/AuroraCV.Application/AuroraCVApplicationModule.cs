using Lion.AbpPro.CacheManagement;
using Lion.AbpPro.CodeManagement;
using Lion.AbpPro.DynamicMenuManagement;
using Lion.AbpPro.FileManagement;
using Lion.AbpPro.ImportExportManagement;
using Lion.AbpPro.MasterDataManagement;
using Lion.AbpPro.TemplateManagement;

namespace AuroraCV
{
    [DependsOn(
        typeof(AuroraCVDomainModule),
        typeof(AuroraCVApplicationContractsModule),
        typeof(BasicManagementApplicationModule),
        typeof(NotificationManagementApplicationModule),
        typeof(DataDictionaryManagementApplicationModule),
        typeof(LanguageManagementApplicationModule),
        typeof(CodeManagementApplicationModule),
        typeof(TemplateManagementApplicationModule),
        typeof(DynamicMenuManagementApplicationModule),
        typeof(FileManagementApplicationModule),
        typeof(ImportExportManagementApplicationModule),
        typeof(CacheManagementApplicationModule),
        typeof(MasterDataManagementApplicationModule)
    )]
    public class AuroraCVApplicationModule : AbpModule { }
}
