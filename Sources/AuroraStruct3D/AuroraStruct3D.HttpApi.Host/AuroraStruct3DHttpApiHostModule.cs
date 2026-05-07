using Lion.AbpPro.CAP;
using Volo.Abp.AspNetCore.Mvc.Libs;
using Volo.Abp.Hangfire;

namespace AuroraStruct3D
{
    [DependsOn(
        typeof(AuroraStruct3DHttpApiModule),
        typeof(AbpProAspNetCoreModule),
        typeof(AuroraStruct3DEntityFrameworkCoreModule),
        typeof(AbpAspNetCoreSerilogModule),
        typeof(AuroraStruct3DApplicationModule),
        typeof(AbpBackgroundJobsHangfireModule),
        typeof(AbpCachingStackExchangeRedisModule),
        typeof(AbpBlobStoringFileSystemModule)
    )]
    public partial class AuroraStruct3DHttpApiHostModule : AbpModule
    {
        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            // 项目已改为纯 Vue SPA，不再依赖 ABP 的 wwwroot/libs 客户端库，禁用启动检查
            Configure<AbpMvcLibsOptions>(options => options.CheckLibs = false);

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
                .AddAbpProCap()
                .AddAbpProHangfire();
        }

        public override void OnApplicationInitialization(ApplicationInitializationContext context)
        {
            var app = context.GetApplicationBuilder();
            app.UseAbpProRequestLocalization();
            //app.UseAbpProRequestResponseEncrypt();
            app.UseCorrelationId();
            app.MapAbpStaticAssets();
            // 启用默认文件（index.html）以承载 Vue SPA
            app.UseDefaultFiles();
            // 明确指定 .html/.js/.css 的 MIME + charset，防止 Windows 浏览器按 GBK 猜测编码
            var provider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
            provider.Mappings[".html"] = "text/html; charset=utf-8";
            provider.Mappings[".js"] = "application/javascript; charset=utf-8";
            provider.Mappings[".mjs"] = "application/javascript; charset=utf-8";
            provider.Mappings[".css"] = "text/css; charset=utf-8";
            app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = provider });
            app.UseAbpProMiniProfiler();
            app.UseRouting();
            app.UseAbpProCors();
            app.UseAuthentication();
            app.UseAbpProMultiTenancy();
            app.UseAuthorization();
            // Hangfire Dashboard 已移除，改为 REST API（由 UseConfiguredEndpoints 内的 MapHangfireApi 注册）
            app.UseAbpProSwaggerUI("/swagger/AbpPro/swagger.json", "AbpPro");
            app.UseAbpProAuditing();
            app.UseAbpSerilogEnrichers();
            app.UseUnitOfWork();
            app.UseConfiguredEndpoints(endpoints =>
            {
                endpoints.MapHealthChecks("/health");
                // 注册 Hangfire 监控 REST API，对应路径前缀 /api/hangfire
                endpoints.UseAbpHangfireApi("/api/hangfire");
                endpoints.MapFallback(async httpContext =>
                {
                    var path = httpContext.Request.Path.Value ?? string.Empty;
                    // 排除 API、内置看板、文档等后端路由
                    if (
                        path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
                        || path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)
                        || path.StartsWith("/hangfire", StringComparison.OrdinalIgnoreCase)
                        || path.StartsWith("/cap", StringComparison.OrdinalIgnoreCase)
                        || path.StartsWith("/profiler", StringComparison.OrdinalIgnoreCase)
                        || path.StartsWith(
                            "/mini-profiler-resources",
                            StringComparison.OrdinalIgnoreCase
                        )
                        || path.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
                        || path.StartsWith("/abp", StringComparison.OrdinalIgnoreCase)
                        || path.StartsWith("/connect", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                        return;
                    }

                    httpContext.Response.ContentType = "text/html; charset=utf-8";
                    var indexPath = Path.Combine(
                        httpContext
                            .RequestServices.GetRequiredService<IWebHostEnvironment>()
                            .WebRootPath
                            ?? "wwwroot",
                        "index.html"
                    );
                    if (File.Exists(indexPath))
                    {
                        await httpContext.Response.SendFileAsync(indexPath);
                    }
                    else
                    {
                        // 前端尚未构建时给出友好提示
                        await httpContext.Response.WriteAsync(
                            "<!doctype html><html><body><h2>SPA 尚未构建</h2>"
                                + "<p>请在 AuroraStruct3D.Frontend 目录运行 <code>npm install &amp;&amp; npm run build</code>，"
                                + "或使用 Release 配置编译后端以触发自动构建。</p></body></html>"
                        );
                    }
                });
            });
            app.UseAbpProConsul();
        }
    }
}
