using Lion.AbpPro.Core;

namespace Lion.AbpPro.DynamicMenuManagement
{
    [DependsOn(typeof(AbpValidationModule), typeof(AbpProCoreModule))]
    public class DynamicMenuManagementDomainSharedModule : AbpModule
    {
        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            Configure<AbpVirtualFileSystemOptions>(options =>
            {
                options.FileSets.AddEmbedded<DynamicMenuManagementDomainSharedModule>();
            });

            Configure<AbpLocalizationOptions>(options =>
            {
                options
                    .Resources.Add<DynamicMenuManagementResource>(
                        DynamicMenuManagementConsts.DefaultCultureName
                    )
                    .AddBaseTypes(typeof(AbpValidationResource))
                    .AddVirtualJson("/Localization/DynamicMenuManagement");
            });

            Configure<AbpExceptionLocalizationOptions>(options =>
            {
                options.MapCodeNamespace(
                    DynamicMenuManagementConsts.NameSpace,
                    typeof(DynamicMenuManagementResource)
                );
            });
        }
    }
}
