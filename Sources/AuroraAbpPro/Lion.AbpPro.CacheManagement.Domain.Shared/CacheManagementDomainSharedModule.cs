using Lion.AbpPro.Core;

namespace Lion.AbpPro.CacheManagement
{
    [DependsOn(typeof(AbpValidationModule), typeof(AbpProCoreModule))]
    public class CacheManagementDomainSharedModule : AbpModule
    {
        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            Configure<AbpVirtualFileSystemOptions>(options =>
            {
                options.FileSets.AddEmbedded<CacheManagementDomainSharedModule>();
            });

            Configure<AbpLocalizationOptions>(options =>
            {
                options
                    .Resources.Add<CacheManagementResource>(
                        CacheManagementConsts.DefaultCultureName
                    )
                    .AddBaseTypes(typeof(AbpValidationResource))
                    .AddVirtualJson("/Localization/CacheManagement");
            });

            Configure<AbpExceptionLocalizationOptions>(options =>
            {
                options.MapCodeNamespace(
                    CacheManagementConsts.NameSpace,
                    typeof(CacheManagementResource)
                );
            });
        }
    }
}
