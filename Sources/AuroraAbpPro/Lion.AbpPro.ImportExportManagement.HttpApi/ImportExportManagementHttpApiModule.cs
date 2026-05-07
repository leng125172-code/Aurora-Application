using Lion.AbpPro.ImportExportManagement.Localization;
using Localization.Resources.AbpUi;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;

namespace Lion.AbpPro.ImportExportManagement
{
    [DependsOn(
        typeof(ImportExportManagementApplicationContractsModule),
        typeof(AbpAspNetCoreMvcModule)
    )]
    public class ImportExportManagementHttpApiModule : AbpModule
    {
        public override void PreConfigureServices(ServiceConfigurationContext context)
        {
            PreConfigure<IMvcBuilder>(mvcBuilder =>
            {
                mvcBuilder.AddApplicationPartIfNotExists(
                    typeof(ImportExportManagementHttpApiModule).Assembly
                );
            });
        }

        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            Configure<AbpLocalizationOptions>(options =>
            {
                options
                    .Resources.Get<ImportExportManagementResource>()
                    .AddBaseTypes(typeof(AbpUiResource));
            });
        }
    }
}
