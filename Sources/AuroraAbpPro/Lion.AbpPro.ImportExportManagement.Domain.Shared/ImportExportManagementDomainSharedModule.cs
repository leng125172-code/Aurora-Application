using Lion.AbpPro.Core;

namespace Lion.AbpPro.ImportExportManagement
{
    [DependsOn(typeof(AbpValidationModule), typeof(AbpProCoreModule))]
    public class ImportExportManagementDomainSharedModule : AbpModule
    {
        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            Configure<AbpVirtualFileSystemOptions>(options =>
            {
                options.FileSets.AddEmbedded<ImportExportManagementDomainSharedModule>();
            });

            Configure<AbpLocalizationOptions>(options =>
            {
                options
                    .Resources.Add<ImportExportManagementResource>(
                        ImportExportManagementConsts.DefaultCultureName
                    )
                    .AddBaseTypes(typeof(AbpValidationResource))
                    .AddVirtualJson("/Localization/ImportExportManagement");
            });

            Configure<AbpExceptionLocalizationOptions>(options =>
            {
                options.MapCodeNamespace(
                    ImportExportManagementConsts.NameSpace,
                    typeof(ImportExportManagementResource)
                );
            });
        }
    }
}
