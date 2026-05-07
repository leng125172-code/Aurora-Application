using Lion.AbpPro.MasterDataManagement.Localization;
using Localization.Resources.AbpUi;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;

namespace Lion.AbpPro.MasterDataManagement
{
    [DependsOn(
        typeof(MasterDataManagementApplicationContractsModule),
        typeof(AbpAspNetCoreMvcModule)
    )]
    public class MasterDataManagementHttpApiModule : AbpModule
    {
        public override void PreConfigureServices(ServiceConfigurationContext context)
        {
            PreConfigure<IMvcBuilder>(mvcBuilder =>
            {
                mvcBuilder.AddApplicationPartIfNotExists(
                    typeof(MasterDataManagementHttpApiModule).Assembly
                );
            });
        }

        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            Configure<AbpLocalizationOptions>(options =>
            {
                options
                    .Resources.Get<MasterDataManagementResource>()
                    .AddBaseTypes(typeof(AbpUiResource));
            });
        }
    }
}
