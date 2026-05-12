using AuroraStruct3D.Tucam;
using Lion.AbpPro.CacheManagement;
using Lion.AbpPro.CodeManagement;
using Lion.AbpPro.DynamicMenuManagement;
using Lion.AbpPro.FileManagement;
using Lion.AbpPro.ImportExportManagement;
using Lion.AbpPro.MasterDataManagement;
using Lion.AbpPro.TemplateManagement;

namespace AuroraStruct3D
{
    [DependsOn(
        typeof(AuroraStruct3DDomainModule),
        typeof(AuroraStruct3DApplicationContractsModule),
        typeof(BasicManagementApplicationModule),
        typeof(NotificationManagementApplicationModule),
        typeof(DataDictionaryManagementApplicationModule),
        typeof(LanguageManagementApplicationModule),
        typeof(CodeManagementApplicationModule),
        typeof(TemplateManagementApplicationModule),
        typeof(DynamicMenuManagementApplicationModule),
        typeof(FileManagementApplicationModule),
        typeof(ImportExportManagementApplicationModule),
        typeof(CacheManagementApplicationModule),
        typeof(MasterDataManagementApplicationModule)
    )]
    public class AuroraStruct3DApplicationModule : AbpModule
    {
        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            // 注册TUCam相机服务为单例
            context.Services.AddTucamCameraServices();
        }
    }
}
