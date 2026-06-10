using AuroraStruct3D.AI;
using AuroraStruct3D.Calibration;
using AuroraStruct3D.Cameras;
using AuroraStruct3D.DeviceState;
using AuroraStruct3D.Motors;
using AuroraStruct3D.ProductModels;
using AuroraStruct3D.Projectors;
using AuroraStruct3D.SerialPorts;
using Lion.AbpPro.BasicManagement.UserRefreshTokens;
using Lion.AbpPro.CodeManagement.DataTypes.Aggregates;
using Lion.AbpPro.CodeManagement.EntityFrameworkCore;
using Lion.AbpPro.CodeManagement.EntityModels.Aggregates;
using Lion.AbpPro.CodeManagement.EnumTypes.Aggregates;
using Lion.AbpPro.CodeManagement.Projects.Aggregates;
using Lion.AbpPro.CodeManagement.Templates.Aggregates;
using Lion.AbpPro.DynamicMenuManagement.EntityFrameworkCore;
using Lion.AbpPro.DynamicMenuManagement.Menus;
using Lion.AbpPro.FileManagement.EntityFrameworkCore;
using Lion.AbpPro.FileManagement.Files;
using Lion.AbpPro.ImportExport.Import;
using Lion.AbpPro.ImportExportManagement.EntityFrameworkCore;
using Lion.AbpPro.MasterDataManagement.EntityFrameworkCore;
using Lion.AbpPro.MasterDataManagement.MasterDataAttributes;
using Lion.AbpPro.MasterDataManagement.MasterDatas;
using Lion.AbpPro.MasterDataManagement.MasterDataTypes;
using Lion.AbpPro.MasterDataManagement.MasterDataValues;
using Lion.AbpPro.TemplateManagement.EntityFrameworkCore;
using Lion.AbpPro.TemplateManagement.TextTemplates;

namespace AuroraStruct3D.EntityFrameworkCore
{
    [ConnectionStringName("Default")]
    public class AuroraStruct3DDbContext
        : AbpDbContext<AuroraStruct3DDbContext>,
            IAuroraStruct3DDbContext,
            IBasicManagementDbContext,
            INotificationManagementDbContext,
            IDataDictionaryManagementDbContext,
            ILanguageManagementDbContext,
            ICodeManagementDbContext,
            ITemplateManagementDbContext,
            IDynamicMenuManagementDbContext,
            IFileManagementDbContext,
            IImportExportManagementDbContext,
            IMasterDataManagementDbContext
    {
        public DbSet<IdentityUser> Users { get; set; }
        public DbSet<IdentityRole> Roles { get; set; }
        public DbSet<IdentityClaimType> ClaimTypes { get; set; }
        public DbSet<OrganizationUnit> OrganizationUnits { get; set; }
        public DbSet<IdentitySecurityLog> SecurityLogs { get; set; }
        public DbSet<IdentityLinkUser> LinkUsers { get; set; }
        public DbSet<IdentityUserDelegation> UserDelegations { get; set; }
        public DbSet<IdentitySession> Sessions { get; set; }
        public DbSet<FeatureGroupDefinitionRecord> FeatureGroups { get; set; }
        public DbSet<FeatureDefinitionRecord> Features { get; set; }
        public DbSet<FeatureValue> FeatureValues { get; set; }
        public DbSet<PermissionGroupDefinitionRecord> PermissionGroups { get; set; }
        public DbSet<PermissionDefinitionRecord> Permissions { get; set; }
        public DbSet<PermissionGrant> PermissionGrants { get; set; }
        public DbSet<ResourcePermissionGrant> ResourcePermissionGrants { get; set; }
        public DbSet<Setting> Settings { get; set; }
        public DbSet<SettingDefinitionRecord> SettingDefinitionRecords { get; set; }
        public DbSet<Tenant> Tenants { get; set; }
        public DbSet<TenantConnectionString> TenantConnectionStrings { get; set; }
        public DbSet<BackgroundJobRecord> BackgroundJobs { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<AuditLogExcelFile> AuditLogExcelFiles { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<NotificationSubscription> NotificationSubscriptions { get; set; }
        public DbSet<DataDictionary> DataDictionaries { get; set; }
        public DbSet<Language> Languages { get; set; }
        public DbSet<LanguageText> LanguageTexts { get; set; }

        public DbSet<Template> Templates { get; set; }

        // 代码生成器模块
        public DbSet<Project> Projects { get; set; }
        public DbSet<EntityModel> EntityModels { get; set; }
        public DbSet<DataType> DataTypes { get; set; }
        public DbSet<EnumType> EnumTypes { get; set; }
        public DbSet<TextTemplate> TextTemplates { get; set; }

        public DbSet<Menu> Menus { get; set; }
        public DbSet<UserRefreshToken> UserRefreshTokens { get; set; }

        public DbSet<FileObject> FileObjects { get; set; }
        public DbSet<ImportRecord> ImportRecords { get; set; }

        public DbSet<MasterDataAttribute> MasterDataAttributes { get; set; }
        public DbSet<MasterData> MasterDatas { get; set; }
        public DbSet<MasterDataType> MasterDataTypes { get; set; }
        public DbSet<MasterDataValue> MasterDataValues { get; set; }

        // ── 标定模块 ──────────────────────────────────────────────────────────────
        public DbSet<CalibProject> CalibProjects { get; set; }
        public DbSet<CalibMotorParam> CalibMotorParams { get; set; }
        public DbSet<CalibMotorConstraint> CalibMotorConstraints { get; set; }
        public DbSet<CalibCameraParam> CalibCameraParams { get; set; }
        public DbSet<CalibProjectorParam> CalibProjectorParams { get; set; }
        public DbSet<CalibDeviceBinding> CalibDeviceBindings { get; set; }
        public DbSet<CalibPhotoRecord> CalibPhotoRecords { get; set; }
        public DbSet<CalibStereoResult> CalibStereoResults { get; set; }

        // ── 串口通讯模块 ────────────────────────────────────────────────────────────
        public DbSet<SerialPortConfig> SerialPortConfigs { get; set; }
        public DbSet<SerialPortOperationLog> SerialPortOperationLogs { get; set; }

        // ── 相机模块 ──────────────────────────────────────────────────────────────
        public DbSet<CameraDevice> CameraDevices { get; set; }
        public DbSet<CameraParameterSet> CameraParameterSets { get; set; }
        public DbSet<CameraParameter> CameraParameters { get; set; }
        public DbSet<CameraOperationLog> CameraOperationLogs { get; set; }

        // ── 电机模块 ──────────────────────────────────────────────────────────────
        public DbSet<MotorAxis> MotorAxes { get; set; }
        public DbSet<MotorMotionConfig> MotorMotionConfigs { get; set; }
        public DbSet<MotorFaultRecord> MotorFaultRecords { get; set; }
        public DbSet<MotorPrPath> MotorPrPaths { get; set; }
        public DbSet<MotorOperationLog> MotorOperationLogs { get; set; }

        // ── DLP 投影机模块 ─────────────────────────────────────────────────────────
        public DbSet<ProjectorDevice> ProjectorDevices { get; set; }
        public DbSet<ProjectorOperationLog> ProjectorOperationLogs { get; set; }

        // ── 设备状态管理模块 ─────────────────────────────────────────────────────────
        public DbSet<DeviceStateLog> DeviceStateLogs { get; set; }
        public DbSet<DeviceFault> DeviceFaults { get; set; }

        // ── 产品三维数模模块 ─────────────────────────────────────────────────────────
        public DbSet<ProductModel> ProductModels { get; set; }
        public DbSet<ProductModelOperationLog> ProductModelOperationLogs { get; set; }

        // ── AI 模型模块 ─────────────────────────────────────────────────────────
        public DbSet<AiModel> AiModels { get; set; }
        public DbSet<AiModelFile> AiModelFiles { get; set; }
        public DbSet<AiModelIdentifier> AiModelIdentifiers { get; set; }
        public DbSet<AiModelIdentifierLink> AiModelIdentifierLinks { get; set; }
        public DbSet<AiModelOperationLog> AiModelOperationLogs { get; set; }

        public AuroraStruct3DDbContext(DbContextOptions<AuroraStruct3DDbContext> options)
            : base(options) { }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.ConfigureAuroraStruct3D();

            // 基础模块
            builder.ConfigureBasicManagement();

            // 消息通知
            builder.ConfigureNotificationManagement();

            //数据字典
            builder.ConfigureDataDictionaryManagement();

            // 多语言
            builder.ConfigureLanguageManagement();

            // 代码生成器模块
            builder.ConfigureCodeManagement();

            // 文本模板模块
            builder.ConfigureTemplateManagement();

            // 动态菜单
            builder.ConfigureDynamicMenuManagement();

            builder.ConfigureFileManagement();

            builder.ConfigureImportExportManagement();

            builder.ConfigureMasterDataManagement();

            // 相机模块
            builder.ConfigureCamera();

            // 串口通讯模块（必须在电机模块之前配置，因为电机轴有外镰关联）
            builder.ConfigureSerialPort();

            // 电机模块
            builder.ConfigureMotor();

            // DLP 投影机模块
            builder.ConfigureProjector();

            // 设备状态管理模块
            builder.ConfigureDeviceState();

            // 产品三维数模模块
            builder.ConfigureProductModel();

            // AI 模型模块
            builder.ConfigureAiModel();

            // 标定模块
            builder.ConfigureCalibration();
        }
    }
}
