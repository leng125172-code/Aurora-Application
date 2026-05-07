using Lion.AbpPro.BasicManagement.UserRefreshTokens;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace Lion.AbpPro.BasicManagement.EntityFrameworkCore;

public static class BasicManagementDbContextModelCreatingExtensions
{
    public static void ConfigureBasicManagement(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<UserRefreshToken>(b =>
        {
            b.ToTable(BasicManagementDbProperties.DbTablePrefix + "UserRefreshTokens");
            b.Property(e => e.RefreshToken).IsRequired().HasMaxLength(128).HasComment("刷新token");
            b.Property(e => e.UserId).HasComment("用户id");
            b.Property(e => e.IsUsed).HasComment("是否使用");
            b.Property(e => e.ExpirationTime).HasComment("过期时间");
            b.Property(e => e.Token).IsRequired().HasMaxLength(1024).HasComment("Token");
            b.HasIndex(e => e.RefreshToken);
            b.ConfigureByConvention();
        });

        builder.ConfigurePermissionManagement();
        builder.ConfigureSettingManagement();
        builder.ConfigureBackgroundJobs();
        builder.ConfigureAuditLogging();
        builder.ConfigureIdentity();
        builder.ConfigureFeatureManagement();
        builder.ConfigureTenantManagement();
    }
}
