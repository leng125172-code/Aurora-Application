using Lion.AbpPro.Core;
using Lion.AbpPro.TemplateManagement.Localization;

namespace Lion.AbpPro.TemplateManagement
{
    [DependsOn(typeof(AbpValidationModule), typeof(AbpProCoreModule))]
    public class TemplateManagementDomainSharedModule : AbpModule
    {
        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            Configure<AbpVirtualFileSystemOptions>(options =>
            {
                options.FileSets.AddEmbedded<TemplateManagementDomainSharedModule>();
            });

            Configure<AbpLocalizationOptions>(options =>
            {
                options
                    .Resources.Add<TemplateManagementResource>(
                        TemplateManagementConsts.DefaultCultureName
                    )
                    .AddBaseTypes(typeof(AbpValidationResource))
                    .AddVirtualJson("/Localization/TemplateManagement");
            });

            Configure<AbpExceptionLocalizationOptions>(options =>
            {
                options.MapCodeNamespace(
                    TemplateManagementConsts.NameSpace,
                    typeof(TemplateManagementResource)
                );
            });
        }
    }
}
