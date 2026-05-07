using Lion.AbpPro.CacheManagement.Localization;
using Localization.Resources.AbpUi;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;

namespace Lion.AbpPro.CacheManagement
{
    [DependsOn(typeof(CacheManagementApplicationContractsModule), typeof(AbpAspNetCoreMvcModule))]
    public class CacheManagementHttpApiModule : AbpModule
    {
        public override void PreConfigureServices(ServiceConfigurationContext context)
        {
            PreConfigure<IMvcBuilder>(mvcBuilder =>
            {
                mvcBuilder.AddApplicationPartIfNotExists(
                    typeof(CacheManagementHttpApiModule).Assembly
                );
            });
        }

        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            Configure<AbpLocalizationOptions>(options =>
            {
                options
                    .Resources.Get<CacheManagementResource>()
                    .AddBaseTypes(typeof(AbpUiResource));
            });
        }
    }
}
