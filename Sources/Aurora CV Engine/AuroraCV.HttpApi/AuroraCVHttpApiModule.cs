using Lion.AbpPro.CacheManagement;
using Lion.AbpPro.CodeManagement;
using Lion.AbpPro.DataDictionaryManagement;
using Lion.AbpPro.DynamicMenuManagement;
using Lion.AbpPro.FileManagement;
using Lion.AbpPro.ImportExportManagement;
using Lion.AbpPro.LanguageManagement;
using Lion.AbpPro.MasterDataManagement;
using Lion.AbpPro.TemplateManagement;

namespace AuroraCV
{
    [DependsOn(
        typeof(AuroraCVApplicationContractsModule),
        typeof(BasicManagementHttpApiModule),
        typeof(NotificationManagementHttpApiModule),
        typeof(DataDictionaryManagementHttpApiModule),
        typeof(LanguageManagementHttpApiModule),
        typeof(CodeManagementHttpApiModule),
        typeof(TemplateManagementHttpApiModule),
        typeof(DynamicMenuManagementHttpApiModule),
        typeof(FileManagementHttpApiModule),
        typeof(CacheManagementHttpApiModule),
        typeof(MasterDataManagementHttpApiModule),
        typeof(ImportExportManagementHttpApiModule)
    )]
    public class AuroraCVHttpApiModule : AbpModule
    {
        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            ConfigureLocalization();
        }

        private void ConfigureLocalization()
        {
            Configure<AbpLocalizationOptions>(options =>
            {
                options.Resources.Get<AuroraCVResource>().AddBaseTypes(typeof(AbpUiResource));
            });
        }
    }
}
