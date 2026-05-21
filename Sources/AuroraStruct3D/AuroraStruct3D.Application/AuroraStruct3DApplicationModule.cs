using AuroraStruct3D.DeviceState;
using AuroraStruct3D.Projectors;
using AuroraStruct3D.RS485;
using AuroraStruct3D.Tucam;
// DeviceStateManagement 项目通迁引用，无需单独 using
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

            // 注册RS485电机控制服务为单例
            context.Services.AddRS485MotorServices();

            // 注册DLP投影机服务为单例
            context.Services.AddDlpProjectorServices();

            // 注册设备状态管理器为单例（来自 AuroraStruct3D.DeviceStateManagement 项目）
            context.Services.AddDeviceStateManagement();
        }
    }
}
