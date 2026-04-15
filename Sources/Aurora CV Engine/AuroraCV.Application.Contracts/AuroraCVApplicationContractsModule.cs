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
        typeof(AbpObjectExtendingModule),
        typeof(BasicManagementApplicationContractsModule),
        typeof(NotificationManagementApplicationContractsModule),
        typeof(DataDictionaryManagementApplicationContractsModule),
        typeof(LanguageManagementApplicationContractsModule),
        typeof(CodeManagementApplicationContractsModule),
        typeof(TemplateManagementApplicationContractsModule),
        typeof(DynamicMenuManagementApplicationContractsModule),
        typeof(FileManagementApplicationContractsModule),
        typeof(CacheManagementApplicationContractsModule),
        typeof(MasterDataManagementApplicationContractsModule),
        typeof(ImportExportManagementApplicationContractsModule)
    )]
    public class AuroraCVApplicationContractsModule : AbpModule
    {
        public override void PreConfigureServices(ServiceConfigurationContext context)
        {
            AuroraCVDtoExtensions.Configure();
        }
    }
}
