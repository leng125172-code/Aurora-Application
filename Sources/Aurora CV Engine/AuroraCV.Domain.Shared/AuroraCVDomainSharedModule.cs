using Lion.AbpPro.CacheManagement;
using Lion.AbpPro.CodeManagement;
using Lion.AbpPro.DynamicMenuManagement;
using Lion.AbpPro.FileManagement;
using Lion.AbpPro.ImportExportManagement;
using Lion.AbpPro.MasterDataManagement;
using Lion.AbpPro.TemplateManagement;

namespace AuroraCV
{
    [DependsOn(
        typeof(BasicManagementDomainSharedModule),
        typeof(NotificationManagementDomainSharedModule),
        typeof(DataDictionaryManagementDomainSharedModule),
        typeof(LanguageManagementDomainSharedModule),
        typeof(CodeManagementDomainSharedModule),
        typeof(TemplateManagementDomainSharedModule),
        typeof(DynamicMenuManagementDomainSharedModule),
        typeof(FileManagementDomainSharedModule),
        typeof(ImportExportManagementDomainSharedModule),
        typeof(CacheManagementDomainSharedModule),
        typeof(MasterDataManagementDomainSharedModule),
        typeof(AbpProCoreModule)
    )]
    public class AuroraCVDomainSharedModule : AbpModule
    {
        public override void PreConfigureServices(ServiceConfigurationContext context)
        {
            AuroraCVGlobalFeatureConfigurator.Configure();
            AuroraCVModuleExtensionConfigurator.Configure();
        }

        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            Configure<AbpVirtualFileSystemOptions>(options =>
            {
                options.FileSets.AddEmbedded<AuroraCVDomainSharedModule>(
                    AuroraCVDomainSharedConsts.NameSpace
                );
            });

            Configure<AbpLocalizationOptions>(options =>
            {
                options
                    .Resources.Add<AuroraCVResource>(AuroraCVDomainSharedConsts.DefaultCultureName)
                    .AddVirtualJson("/Localization/AuroraCV")
                    .AddBaseTypes(typeof(BasicManagementResource))
                    .AddBaseTypes(typeof(AbpTimingResource));

                options.DefaultResourceType = typeof(AuroraCVResource);
            });

            Configure<AbpExceptionLocalizationOptions>(options =>
            {
                options.MapCodeNamespace(
                    AuroraCVDomainSharedConsts.NameSpace,
                    typeof(AuroraCVResource)
                );
            });
        }
    }
}
