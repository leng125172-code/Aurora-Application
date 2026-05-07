using Lion.AbpPro.DataDictionaryManagement.Localization;
using Localization.Resources.AbpUi;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;

namespace Lion.AbpPro.DataDictionaryManagement
{
    [DependsOn(
        typeof(DataDictionaryManagementApplicationContractsModule),
        typeof(AbpAspNetCoreMvcModule)
    )]
    public class DataDictionaryManagementHttpApiModule : AbpModule
    {
        public override void PreConfigureServices(ServiceConfigurationContext context)
        {
            PreConfigure<IMvcBuilder>(mvcBuilder =>
            {
                mvcBuilder.AddApplicationPartIfNotExists(
                    typeof(DataDictionaryManagementHttpApiModule).Assembly
                );
            });
        }

        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            Configure<AbpLocalizationOptions>(options =>
            {
                options
                    .Resources.Get<DataDictionaryManagementResource>()
                    .AddBaseTypes(typeof(AbpUiResource));
            });
        }
    }
}
