using Lion.AbpPro.BasicManagement.UserRefreshTokens;

namespace Lion.AbpPro.BasicManagement.EntityFrameworkCore;

[ConnectionStringName(BasicManagementDbProperties.ConnectionStringName)]
public interface IBasicManagementDbContext
    : IEfCoreDbContext,
        IFeatureManagementDbContext,
        IIdentityDbContext,
        IPermissionManagementDbContext,
        ISettingManagementDbContext,
        ITenantManagementDbContext,
        IBackgroundJobsDbContext,
        IAuditLoggingDbContext
{
    DbSet<UserRefreshToken> UserRefreshTokens { get; set; }
}
