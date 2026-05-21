using AuroraStruct3D.Cameras;
using AuroraStruct3D.DeviceState;
using AuroraStruct3D.Motors;
using AuroraStruct3D.Projectors;
using AuroraStruct3D.SerialPorts;
using Lion.AbpPro.CodeManagement.EntityFrameworkCore;
using Lion.AbpPro.DynamicMenuManagement.EntityFrameworkCore;
using Lion.AbpPro.FileManagement.EntityFrameworkCore;
using Lion.AbpPro.ImportExportManagement.EntityFrameworkCore;
using Lion.AbpPro.MasterDataManagement.EntityFrameworkCore;
using Lion.AbpPro.TemplateManagement.EntityFrameworkCore;
using Volo.Abp.Guids;

namespace AuroraStruct3D.EntityFrameworkCore
{
    [DependsOn(
        typeof(AuroraStruct3DDomainModule),
        typeof(AbpEntityFrameworkCorePostgreSqlModule),
        typeof(BasicManagementEntityFrameworkCoreModule),
        typeof(DataDictionaryManagementEntityFrameworkCoreModule),
        typeof(NotificationManagementEntityFrameworkCoreModule),
        typeof(LanguageManagementEntityFrameworkCoreModule),
        typeof(CodeManagementEntityFrameworkCoreModule),
        typeof(TemplateManagementEntityFrameworkCoreModule),
        typeof(DynamicMenuManagementEntityFrameworkCoreModule),
        typeof(FileManagementEntityFrameworkCoreModule),
        typeof(MasterDataManagementEntityFrameworkCoreModule),
        typeof(ImportExportManagementEntityFrameworkCoreModule)
    )]
    public class AuroraStruct3DEntityFrameworkCoreModule : AbpModule
    {
        public override void PreConfigureServices(ServiceConfigurationContext context)
        {
            AuroraStruct3DEfCoreEntityExtensionMappings.Configure();
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        }

        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            context.Services.AddAbpDbContext<AuroraStruct3DDbContext>(options =>
            {
                /* Remove "includeAllEntities: true" to create
                 * default repositories only for aggregate roots */
                options.AddDefaultRepositories(includeAllEntities: true);

                // 注册串口通讯模块自定义仓储
                options.AddRepository<SerialPortConfig, EfCoreSerialPortConfigRepository>();

                // 注册相机模块自定义仓储
                options.AddRepository<CameraDevice, EfCoreCameraDeviceRepository>();
                options.AddRepository<CameraParameterSet, EfCoreCameraParameterSetRepository>();
                options.AddRepository<CameraOperationLog, EfCoreCameraOperationLogRepository>();

                // 注册电机模块自定义仓储
                options.AddRepository<MotorAxis, EfCoreMotorAxisRepository>();
                options.AddRepository<MotorPrPath, EfCoreMotorPrPathRepository>();
                options.AddRepository<MotorOperationLog, EfCoreMotorOperationLogRepository>();

                // 注册 DLP 投影机模块自定义仓储
                options.AddRepository<ProjectorDevice, EfCoreProjectorDeviceRepository>();
                options.AddRepository<
                    ProjectorOperationLog,
                    EfCoreProjectorOperationLogRepository
                >();

                // 注册设备状态管理模块自定义仓储
                options.AddRepository<DeviceStateLog, EfCoreDeviceStateLogRepository>();
            });
            Configure<AbpSequentialGuidGeneratorOptions>(options =>
            {
                options.DefaultSequentialGuidType = SequentialGuidType.SequentialAsString;
            });
            Configure<AbpDbContextOptions>(options =>
            {
                /* The main point to change your DBMS.
                 * See also HayoonKoreaDbContextFactory for EF Core tooling.
                 *  https://github.com/abpframework/abp/issues/21879
                 * */
                options.UseNpgsql();
            });
        }
    }
}
