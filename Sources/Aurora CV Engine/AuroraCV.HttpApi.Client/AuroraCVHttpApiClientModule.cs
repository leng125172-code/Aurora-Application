using Lion.AbpPro.BasicManagement;
using Lion.AbpPro.CacheManagement;
using Lion.AbpPro.CodeManagement;
using Lion.AbpPro.DataDictionaryManagement;
using Lion.AbpPro.DynamicMenuManagement;
using Lion.AbpPro.FileManagement;
using Lion.AbpPro.ImportExportManagement;
using Lion.AbpPro.LanguageManagement;
using Lion.AbpPro.MasterDataManagement;
using Lion.AbpPro.NotificationManagement;
using Lion.AbpPro.TemplateManagement;

namespace AuroraCV
{
    [DependsOn(
        typeof(AuroraCVApplicationContractsModule),
        typeof(BasicManagementHttpApiClientModule),
        typeof(NotificationManagementHttpApiClientModule),
        typeof(DataDictionaryManagementHttpApiClientModule),
        typeof(LanguageManagementHttpApiClientModule),
        typeof(CodeManagementHttpApiClientModule),
        typeof(TemplateManagementHttpApiClientModule),
        typeof(DynamicMenuManagementHttpApiClientModule),
        typeof(FileManagementHttpApiClientModule),
        typeof(CacheManagementHttpApiClientModule),
        typeof(MasterDataManagementHttpApiClientModule),
        typeof(ImportExportManagementHttpApiClientModule)
    )]
    public class AuroraCVHttpApiClientModule : AbpModule
    {
        public const string RemoteServiceName = "Default";

        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            context.Services.AddHttpClientProxies(
                typeof(AuroraCVApplicationContractsModule).Assembly,
                RemoteServiceName
            );
        }
    }
}
