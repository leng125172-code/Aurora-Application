using AuroraStruct3D.AI;
using AuroraStruct3D.DeviceState;
using AuroraStruct3D.Ktech;
using AuroraStruct3D.Leisai;
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
using Microsoft.Extensions.DependencyInjection;

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
            // 注册 AI 模型运行时服务为单例
            context.Services.AddAiServices();

            // 注册TUCam相机服务为单例
            context.Services.AddTucamCameraServices();

            // 注册RS485电机控制服务为单例
            context.Services.AddRS485MotorServices();

            // 注册DLP投影机服务为单例
            context.Services.AddDlpProjectorServices();

            // 注册设备状态管理器为单例（来自 AuroraStruct3D.DeviceStateManagement 项目）
            context.Services.AddDeviceStateManagement();

            // 注册瓴控 KTECH 实时数据采集后台服务
            // KtechSamplerStateStore 作为单例共享给 HostedService 与 AppService
            context.Services.AddSingleton<KtechSamplerStateStore>();
            context.Services.AddHostedService<KtechSamplerHostedService>();

            // 注册雷赛 iCL-RS 实时数据采集后台服务
            context.Services.AddSingleton<LeisaiSamplerStateStore>();
            context.Services.AddHostedService<LeisaiSamplerHostedService>();

            // 注册 Step6 在线扫描会话状态存储（进程内共享）
            context.Services.AddSingleton<Calibration.CalibScanStateStore>();

            // 注册 Step7 点云生成会话状态存储（进程内共享）
            context.Services.AddSingleton<Calibration.CalibPointCloudStateStore>();
        }
    }
}
