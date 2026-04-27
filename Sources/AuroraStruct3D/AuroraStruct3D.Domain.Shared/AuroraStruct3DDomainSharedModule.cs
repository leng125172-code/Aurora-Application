using Lion.AbpPro.CodeManagement;
using Lion.AbpPro.DynamicMenuManagement;
using Lion.AbpPro.FileManagement;
using Lion.AbpPro.ImportExportManagement;
using Lion.AbpPro.TemplateManagement;
using Lion.AbpPro.CacheManagement;
using Lion.AbpPro.MasterDataManagement;

namespace AuroraStruct3D
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
    public class AuroraStruct3DDomainSharedModule : AbpModule
    {
        public override void PreConfigureServices(ServiceConfigurationContext context)
        {
            AuroraStruct3DGlobalFeatureConfigurator.Configure();
            AuroraStruct3DModuleExtensionConfigurator.Configure();
        }

        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            Configure<AbpVirtualFileSystemOptions>(options =>
            {
                options.FileSets.AddEmbedded<AuroraStruct3DDomainSharedModule>(AuroraStruct3DDomainSharedConsts.NameSpace);
            });
          
            Configure<AbpLocalizationOptions>(options =>
            {
                options.Resources
                    .Add<AuroraStruct3DResource>(AuroraStruct3DDomainSharedConsts.DefaultCultureName)
                    .AddVirtualJson("/Localization/AuroraStruct3D")
                    .AddBaseTypes(typeof(BasicManagementResource))
                    .AddBaseTypes(typeof(AbpTimingResource));

                options.DefaultResourceType = typeof(AuroraStruct3DResource);
            });

            Configure<AbpExceptionLocalizationOptions>(options =>
            {
                options.MapCodeNamespace(AuroraStruct3DDomainSharedConsts.NameSpace, typeof(AuroraStruct3DResource));
            });
        }

       
    }
}