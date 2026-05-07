using Lion.AbpPro.TemplateManagement.Localization;
using Localization.Resources.AbpUi;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;

namespace Lion.AbpPro.TemplateManagement
{
    [DependsOn(
        typeof(TemplateManagementApplicationContractsModule),
        typeof(AbpAspNetCoreMvcModule)
    )]
    public class TemplateManagementHttpApiModule : AbpModule
    {
        public override void PreConfigureServices(ServiceConfigurationContext context)
        {
            PreConfigure<IMvcBuilder>(mvcBuilder =>
            {
                mvcBuilder.AddApplicationPartIfNotExists(
                    typeof(TemplateManagementHttpApiModule).Assembly
                );
            });
        }

        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            Configure<AbpLocalizationOptions>(options =>
            {
                options
                    .Resources.Get<TemplateManagementResource>()
                    .AddBaseTypes(typeof(AbpUiResource));
            });
        }
    }
}
