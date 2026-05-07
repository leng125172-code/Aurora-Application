using Lion.AbpPro.Core;

namespace Lion.AbpPro.CodeManagement
{
    [DependsOn(typeof(AbpValidationModule), typeof(AbpProCoreModule))]
    public class CodeManagementDomainSharedModule : AbpModule
    {
        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            Configure<AbpVirtualFileSystemOptions>(options =>
            {
                options.FileSets.AddEmbedded<CodeManagementDomainSharedModule>();
            });

            Configure<AbpLocalizationOptions>(options =>
            {
                options
                    .Resources.Add<CodeManagementResource>(CodeManagementConsts.DefaultCultureName)
                    .AddBaseTypes(typeof(AbpValidationResource))
                    .AddVirtualJson("/Localization/CodeManagement");
            });

            Configure<AbpExceptionLocalizationOptions>(options =>
            {
                options.MapCodeNamespace(
                    CodeManagementConsts.NameSpace,
                    typeof(CodeManagementResource)
                );
            });
        }
    }
}
