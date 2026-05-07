using Lion.AbpPro.DynamicMenuManagement.Localization;
using Localization.Resources.AbpUi;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;

namespace Lion.AbpPro.DynamicMenuManagement
{
    [DependsOn(
        typeof(DynamicMenuManagementApplicationContractsModule),
        typeof(AbpAspNetCoreMvcModule)
    )]
    public class DynamicMenuManagementHttpApiModule : AbpModule
    {
        public override void PreConfigureServices(ServiceConfigurationContext context)
        {
            PreConfigure<IMvcBuilder>(mvcBuilder =>
            {
                mvcBuilder.AddApplicationPartIfNotExists(
                    typeof(DynamicMenuManagementHttpApiModule).Assembly
                );
            });
        }

        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            Configure<AbpLocalizationOptions>(options =>
            {
                options
                    .Resources.Get<DynamicMenuManagementResource>()
                    .AddBaseTypes(typeof(AbpUiResource));
            });
        }
    }
}
