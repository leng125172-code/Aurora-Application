using System.Net;
using AuroraStruct3D.AI;
using AuroraStruct3D.Cameras;
using AuroraStruct3D.Endpoints;
using AuroraStruct3D.HostedServices;
using AuroraStruct3D.Hubs;
using AuroraStruct3D.Jobs;
using AuroraStruct3D.Motors;
using AuroraStruct3D.Notifiers;
using AuroraStruct3D.Plcs;
using AuroraStruct3D.Projectors;
using AuroraStruct3D.RS485;
using AuroraStruct3D.Services;
using AuroraStruct3D.Sessions;
using AuroraStruct3D.Streaming;
using AuroraStruct3D.Cameras.Tucam;
using Hangfire;
using Lion.AbpPro.CAP;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Volo.Abp.AspNetCore.ExceptionHandling;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc.Libs;
using Volo.Abp.BackgroundJobs;
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
        public override void PreConfigureServices(ServiceConfigurationContext context)
        {
            // 将 AuroraStruct3D 应用层的 AppService 注册为 ABP 动态 API 控制器
            PreConfigure<AbpAspNetCoreMvcOptions>(options =>
            {
                options.ConventionalControllers.Create(
                    typeof(AuroraStruct3DApplicationModule).Assembly
                );
            });
        }

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
                .AddAbpProBlobStorageFileSystem(context.Services.GetConfiguration())
                .AddAbpProSignalR()
                .AddAbpProHealthChecks()
                .AddAbpProTenantResolvers()
                .AddAbpProLocalization()
                .AddAbpProExceptions()
                .AddAbpProSwagger("AbpPro")
                .AddAbpProCap()
                .AddAbpProHangfire();

            // 移除 Kestrel 上传大小限制，支持大型三维文件上传
            context.Services.Configure<KestrelServerOptions>(options =>
            {
                options.Limits.MaxRequestBodySize = null;
            });

            context.Services.Configure<FormOptions>(options =>
            {
                options.MultipartBodyLengthLimit = long.MaxValue;
                options.MultipartHeadersLengthLimit = int.MaxValue;
            });

            // 注册后台广播服务，定期通过 SignalR 推送仪表盘统计数据
            context.Services.AddHostedService<DashboardBroadcastService>();
            // 注册设备状态推送服务，订阅 IDeviceStateManager 事件并实时广播给 DeviceStateHub 客户端
            context.Services.AddHostedService<DeviceStatePushService>();
            // 注册系统指标采集器（单例，维护两次采样之间的状态）
            context.Services.AddSingleton<SystemMetricsCollector>();
            context.Services.AddSingleton<IPlcRealtimeNotifier, SignalRPlcRealtimeNotifier>();

            // 注册 RTP/MJPEG UDP 推流服务器（单例）
            context.Services.AddSingleton<RtpMjpegServer>();
            // 注册 HTTP MJPEG 流帧缓冲服务（单例）
            context.Services.AddSingleton<CameraFrameBufferService>();
            context.Services.AddSingleton<CalibScanFrameBufferService>();
            // 注册相机实时预览服务（同时实现 ICameraStreamingService 和 IHostedService）
            context.Services.AddSingleton<CameraPreviewService>();
            context.Services.AddSingleton<ICameraStreamingService>(sp =>
                sp.GetRequiredService<CameraPreviewService>()
            );
            context.Services.AddHostedService(sp => sp.GetRequiredService<CameraPreviewService>());

            // 注册设备会话广播器（单例）：订阅 IDeviceOperationSessionManager.SessionChanged 并实时推送至 SignalR
            context.Services.AddSingleton<DeviceSessionBroadcaster>();

            // 映射 AuroraStruct3D:DeviceOccupied 错误代码为 HTTP 409 Conflict
            Configure<AbpExceptionHttpStatusCodeOptions>(options =>
                options.Map(AuroraStruct3DDomainErrorCodes.DeviceOccupied, HttpStatusCode.Conflict)
            );

            // 为 SignalR 启用 MessagePack 协议（在 AddAbpProSignalR 之后调用）
            // 配置 MaximumReceiveMessageSize=10MB 以支持 Step6 扫描原图二进制推送（2448×2048 JPEG 约 1-2MB/帧）
            context
                .Services.AddSignalR(o => o.MaximumReceiveMessageSize = 10 * 1024 * 1024)
                .AddMessagePackProtocol();
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
                // 注册系统信息 REST API
                endpoints.MapSystemInfoApi();
                // 注册 MiniProfiler 自定义查询 API（全量会话列表和详情）
                endpoints.MapProfilerApi();
                // 注册相机 MJPEG HTTP 流端点
                endpoints.MapCameraStreamingApi();
                // 直接映射 DashboardHub，无需依赖 AbpAspNetCoreSignalRModule
                endpoints.MapHub<DashboardHub>("/signalr-hubs/dashboard");
                // 映射设备状态实时推送 Hub（允许匿名访问，登录前后均可连接）
                endpoints.MapHub<DeviceStateHub>("/signalr-hubs/device-state");
                endpoints.MapHub<ProjectorHub>("/signalr-hubs/projector");
                endpoints.MapHub<CameraHub>("/signalr-hubs/camera");
                endpoints.MapHub<MotorScanHub>("/signalr-hubs/motor-scan");
                endpoints.MapHub<KtechMotorHub>("/signalr-hubs/ktech-motor");
                endpoints.MapHub<LeisaiMotorHub>("/signalr-hubs/leisai-motor");
                endpoints.MapHub<PlcHub>("/signalr-hubs/plc");
                // 产品数模转换进度推送 Hub
                endpoints.MapHub<ProductModelHub>("/signalr-hubs/product-model");
                // AI 模型转换进度推送 Hub
                endpoints.MapHub<AiModelConversionHub>("/signalr-hubs/ai-model-conversion");
                endpoints.MapHub<WorkflowDebugHub>("/signalr-hubs/workflow-debug");
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
                        || path.StartsWith("/signalr-hubs", StringComparison.OrdinalIgnoreCase)
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

            // 从数据库读取串口配置和电机轴，初始化 MotorControlService
            using (IServiceScope scope = context.ServiceProvider.CreateScope())
            {
                IMotorAxisRepository motorRepo =
                    scope.ServiceProvider.GetRequiredService<IMotorAxisRepository>();
                IMotorControlService motorService =
                    scope.ServiceProvider.GetRequiredService<IMotorControlService>();

                // GetEnabledListAsync 已通过 Include 加载 SerialPortConfig 导航属性
                List<MotorAxis> axes = motorRepo.GetEnabledListAsync().GetAwaiter().GetResult();

                // 从轴中提取去重的串口配置列表（排除导航属性未加载的异常情况）
                var portConfigs = axes.Where(a => a.SerialPortConfig != null)
                    .Select(a => a.SerialPortConfig!)
                    .DistinctBy(p => p.Id)
                    .ToList();

                // 用数据库配置创建串口实例和电机驱动
                motorService.Initialize(portConfigs, axes);

                // 注入 SlaveId → MotorAxis.Id 映射（用于操作日志写入数据库）
                Dictionary<int, Guid> mapping = axes.ToDictionary(a => a.SlaveId, a => a.Id);
                motorService.SetAxisIdMapping(mapping);
            }

            // 初始化相机设备 ID 映射（用于 TucamCameraService 写入操作日志）
            using (IServiceScope scope = context.ServiceProvider.CreateScope())
            {
                ICameraDeviceRepository cameraRepo =
                    scope.ServiceProvider.GetRequiredService<ICameraDeviceRepository>();
                ICameraDriverRegistry cameraDrivers =
                    scope.ServiceProvider.GetRequiredService<ICameraDriverRegistry>();

                List<CameraDevice> cameras = cameraRepo
                    .GetEnabledListAsync()
                    .GetAwaiter()
                    .GetResult();
                if (
                    cameraDrivers.TryGet("tucam", out ICameraDriver? driver)
                    && driver is ITucamCameraService tucam
                )
                {
                    Dictionary<int, Guid> cameraMapping = cameras
                        .Where(c =>
                            string.Equals(c.DriverId, "tucam", StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(c.HardwareId)
                            && driver.TryGetRuntimeIndex(c.HardwareId, out _)
                        )
                        .ToDictionary(
                            c =>
                            {
                                driver.TryGetRuntimeIndex(c.HardwareId!, out int index);
                                return index;
                            },
                            c => c.Id
                        );
                    tucam.SetCameraDeviceIdMapping(cameraMapping);
                }
            }

            // 初始化投影仪设备 ID 映射（用于 DlpProjectorService 写入操作日志）
            using (IServiceScope scope = context.ServiceProvider.CreateScope())
            {
                IProjectorDeviceRepository projectorRepo =
                    scope.ServiceProvider.GetRequiredService<IProjectorDeviceRepository>();
                IDlpProjectorService projectorService =
                    scope.ServiceProvider.GetRequiredService<IDlpProjectorService>();

                List<ProjectorDevice> projectors = projectorRepo
                    .GetEnabledListAsync()
                    .GetAwaiter()
                    .GetResult();

                Dictionary<int, Guid> projectorMapping = projectors
                    .Where(p => p.ConnectionType == ProjectorConnectionType.UsbHid)
                    .ToDictionary(p => p.HidDeviceIndex, p => p.Id);

                projectorService.SetProjectorDeviceIdMapping(projectorMapping);
            }
        }

        /// <summary>
        /// 异步初始化钩子：在同步初始化完成后，通过 Hangfire 触发相机和投影仪扫描 Job。
        /// </summary>
        public override async Task OnApplicationInitializationAsync(
            ApplicationInitializationContext context
        )
        {
            await base.OnApplicationInitializationAsync(context);

            IBackgroundJobManager jobManager =
                context.ServiceProvider.GetRequiredService<IBackgroundJobManager>();

            // 触发相机 SDK 初始化 + 扫描 Job
            await jobManager.EnqueueAsync(new CameraInitScanJobArgs());

            // 触发投影仪扫描 Job
            await jobManager.EnqueueAsync(new ProjectorInitScanJobArgs());

            // 注册定时清理过期算子文件 Job（每小时执行一次）
            IRecurringJobManager recurringJobManager =
                context.ServiceProvider.GetRequiredService<IRecurringJobManager>();
            recurringJobManager.AddOrUpdate<CleanupExpiredOperatorFilesJob>(
                "cleanup-expired-operator-files",
                job => job.ExecuteAsync(new CleanupExpiredOperatorFilesJobArgs()),
                Cron.Hourly
            );
        }
    }
}
