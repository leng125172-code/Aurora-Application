using Lion.AbpPro.Core;

namespace Lion.AbpPro.MasterDataManagement
{
    [DependsOn(typeof(AbpValidationModule), typeof(AbpProCoreModule))]
    public class MasterDataManagementDomainSharedModule : AbpModule
    {
        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            Configure<AbpVirtualFileSystemOptions>(options =>
            {
                options.FileSets.AddEmbedded<MasterDataManagementDomainSharedModule>();
            });

            Configure<AbpLocalizationOptions>(options =>
            {
                options
                    .Resources.Add<MasterDataManagementResource>(
                        MasterDataManagementConsts.DefaultCultureName
                    )
                    .AddBaseTypes(typeof(AbpValidationResource))
                    .AddVirtualJson("/Localization/MasterDataManagement");
            });

            Configure<AbpExceptionLocalizationOptions>(options =>
            {
                options.MapCodeNamespace(
                    MasterDataManagementConsts.NameSpace,
                    typeof(MasterDataManagementResource)
                );
            });
        }
    }
}
