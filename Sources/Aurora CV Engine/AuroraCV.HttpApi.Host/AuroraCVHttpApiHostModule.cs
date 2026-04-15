using Lion.AbpPro.CAP;
using Volo.Abp.Hangfire;

namespace AuroraCV
{
    [DependsOn(
        typeof(AuroraCVHttpApiModule),
        typeof(AbpProAspNetCoreModule),
        typeof(AuroraCVEntityFrameworkCoreModule),
        typeof(AbpAspNetCoreSerilogModule),
        typeof(AbpAccountWebModule),
        typeof(AuroraCVApplicationModule),
        typeof(AbpCachingStackExchangeRedisModule),
        typeof(AbpBlobStoringFileSystemModule),

        typeof(AbpHangfireModule),
        typeof(AbpBackgroundJobsHangfireModule),
        typeof(AbpProCapModule)
    )]
    public partial class AuroraCVHttpApiHostModule : AbpModule
    {
        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            context
                .Services.AddAbpProAuditLog()
                .AddAbpProJwtBearer()
                .AddAbpProMultiTenancy()
                .AddAbpProRedis()
                .AddAbpProMiniProfiler()
                .AddAbpProCors()
                .AddAbpProAntiForgery()
                .AddAbpProIdentity()
                .AddAbpProBlobStorageFileSystem()
                .AddAbpProSignalR()
                .AddAbpProHealthChecks()
                .AddAbpProTenantResolvers()
                .AddAbpProLocalization()
                .AddAbpProExceptions()
                .AddAbpProSwagger("AbpPro")

                // 👇 在这里注册 CAP + Hangfire
                .AddAbpProCap()
                .AddAbpProHangfire();
        }

        public override void OnApplicationInitialization(ApplicationInitializationContext context)
        {
            var app = context.GetApplicationBuilder();
            app.UseAbpProRequestLocalization();
            app.UseCorrelationId();
            app.MapAbpStaticAssets();
            app.UseAbpProMiniProfiler();
            app.UseRouting();
            app.UseAbpProCors();
            app.UseAuthentication();
            app.UseAbpProMultiTenancy();
            app.UseAuthorization();

            app.UseAbpHangfireDashboard();

            app.UseAbpProSwaggerUI("/swagger/AbpPro/swagger.json", "AbpPro");
            app.UseAbpProAuditing();
            app.UseAbpSerilogEnrichers();
            app.UseUnitOfWork();
            app.UseConfiguredEndpoints(endpoints =>
            {
                endpoints.MapHealthChecks("/health");
            });
            app.UseAbpProConsul();
        }
    }
}
