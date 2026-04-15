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
        typeof(AuroraCVDomainSharedModule),
        typeof(BasicManagementDomainModule),
        typeof(NotificationManagementDomainModule),
        typeof(DataDictionaryManagementDomainModule),
        typeof(LanguageManagementDomainModule),
        typeof(CodeManagementDomainModule),
        typeof(TemplateManagementDomainModule),
        typeof(DynamicMenuManagementDomainModule),
        typeof(FileManagementDomainModule),
        typeof(CacheManagementDomainModule),
        typeof(MasterDataManagementDomainModule),
        typeof(ImportExportManagementDomainModule)
    )]
    public class AuroraCVDomainModule : AbpModule { }
}
