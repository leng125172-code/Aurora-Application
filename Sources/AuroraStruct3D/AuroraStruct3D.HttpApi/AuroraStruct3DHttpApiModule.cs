using Lion.AbpPro.CacheManagement;
using Lion.AbpPro.CodeManagement;
using Lion.AbpPro.DataDictionaryManagement;
using Lion.AbpPro.DynamicMenuManagement;
using Lion.AbpPro.FileManagement;
using Lion.AbpPro.ImportExportManagement;
using Lion.AbpPro.LanguageManagement;
using Lion.AbpPro.MasterDataManagement;
using Lion.AbpPro.TemplateManagement;

namespace AuroraStruct3D
{
    [DependsOn(
        typeof(AuroraStruct3DApplicationContractsModule),
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
    public class AuroraStruct3DHttpApiModule : AbpModule
    {
        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            ConfigureLocalization();
        }

        private void ConfigureLocalization()
        {
            Configure<AbpLocalizationOptions>(options =>
            {
                options.Resources.Get<AuroraStruct3DResource>().AddBaseTypes(typeof(AbpUiResource));
            });
        }
    }
}
