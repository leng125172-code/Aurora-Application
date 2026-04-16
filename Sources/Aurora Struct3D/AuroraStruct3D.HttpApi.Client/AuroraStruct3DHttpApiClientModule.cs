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

namespace AuroraStruct3D
{
    [DependsOn(
        typeof(AuroraStruct3DApplicationContractsModule),
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
    public class AuroraStruct3DHttpApiClientModule : AbpModule
    {
        public const string RemoteServiceName = "Default";

        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            context.Services.AddHttpClientProxies(
                typeof(AuroraStruct3DApplicationContractsModule).Assembly,
                RemoteServiceName
            );
        }
    }
}
