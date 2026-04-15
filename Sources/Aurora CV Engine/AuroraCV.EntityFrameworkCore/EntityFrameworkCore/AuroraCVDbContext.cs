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

namespace AuroraCV.EntityFrameworkCore
{
    [ConnectionStringName("Default")]
    public class AuroraCVDbContext
        : AbpDbContext<AuroraCVDbContext>,
            IAuroraCVDbContext,
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

        public AuroraCVDbContext(DbContextOptions<AuroraCVDbContext> options)
            : base(options) { }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.ConfigureAuroraCV();

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
        }
    }
}
