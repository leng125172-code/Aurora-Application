using Lion.AbpPro.CodeManagement.Localization;
using Localization.Resources.AbpUi;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;

namespace Lion.AbpPro.CodeManagement
{
    [DependsOn(typeof(CodeManagementApplicationContractsModule), typeof(AbpAspNetCoreMvcModule))]
    public class CodeManagementHttpApiModule : AbpModule
    {
        public override void PreConfigureServices(ServiceConfigurationContext context)
        {
            PreConfigure<IMvcBuilder>(mvcBuilder =>
            {
                mvcBuilder.AddApplicationPartIfNotExists(
                    typeof(CodeManagementHttpApiModule).Assembly
                );
            });
        }

        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            Configure<AbpLocalizationOptions>(options =>
            {
                options.Resources.Get<CodeManagementResource>().AddBaseTypes(typeof(AbpUiResource));
            });
        }
    }
}
