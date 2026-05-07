using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Aurora.AbpPro.Tools.Models;
using Aurora.AbpPro.Tools.Pages;
using Aurora.AbpPro.Tools.Services;
using Aurora.AbpPro.Tools.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;

namespace Aurora.AbpPro.Tools;

/// <summary>
/// 应用入口：基于 .NET Generic Host 构建应用主机，统一管理依赖注入、配置、日志与生命周期。
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 应用主机实例
    /// </summary>
    private IHost? _host;

    /// <summary>
    /// 全局服务定位器（在主窗口显示前已就绪）
    /// </summary>
    public static IServiceProvider Services { get; private set; } = default!;

    /// <summary>
    /// 应用启动事件：构建主机、解析主窗口、注册全局异常
    /// </summary>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 注册全局异常捕获
        RegisterGlobalExceptionHandlers();

        // 构建并启动 Host
        _host = CreateHostBuilder(e.Args).Build();
        Services = _host.Services;
        await _host.StartAsync();

        // 应用初始配置（皮肤、语言）
        var settings = Services.GetRequiredService<IAppSettingsService>().Current;
        Services.GetRequiredService<IThemeService>().ApplySkin(settings.Skin);
        Services.GetRequiredService<ILocalizationService>().ApplyLanguage(settings.Language);

        // 显示主窗口
        var mainWindow = Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    /// <summary>
    /// 应用退出事件：优雅关闭 Host，刷新 NLog
    /// </summary>
    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            using (_host)
            {
                await _host.StopAsync(System.TimeSpan.FromSeconds(5));
            }
        }
        NLog.LogManager.Shutdown();
        base.OnExit(e);
    }

    /// <summary>
    /// 创建并配置 Generic Host：注册配置、日志、服务
    /// </summary>
    private static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration(
                (_, builder) =>
                {
                    // 加载 appsettings.json 配置
                    builder
                        .SetBasePath(AppContext.BaseDirectory)
                        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                }
            )
            .ConfigureLogging(
                (_, logging) =>
                {
                    // 接入 NLog
                    logging.ClearProviders();
                    logging.SetMinimumLevel(LogLevel.Trace);
                    logging.AddNLog(Path.Combine(AppContext.BaseDirectory, "NLog.config"));
                }
            )
            .ConfigureServices(
                (context, services) =>
                {
                    // 绑定配置选项
                    services.Configure<AppSettings>(
                        context.Configuration.GetSection("AppSettings")
                    );

                    // 注册基础服务（单例）
                    services.AddSingleton<IAppSettingsService, AppSettingsService>();
                    services.AddSingleton<IThemeService, ThemeService>();
                    services.AddSingleton<ILocalizationService, LocalizationService>();
                    services.AddSingleton<INavigationService, NavigationService>();
                    services.AddSingleton<IUiLogSink, UiLogSink>();
                    services.AddSingleton<IGithubClientService, GithubClientService>();
                    services.AddSingleton<IFrameworkUpdateService, FrameworkUpdateService>();
                    services.AddSingleton<IFrameworkBuildService, FrameworkBuildService>();
                    services.AddSingleton<IFrameworkDownloadService, FrameworkDownloadService>();
                    services.AddSingleton<IFrameworkReadService, FrameworkReadService>();
                    services.AddSingleton<IFrameworkGenerateService, FrameworkGenerateService>();
                    services.AddSingleton<ReadonlyConfig>();

                    // 注册仓库配置（多个实例，单例集合）
                    // 仓库 1：abp-vnext-pro/abp（商业版主仓库，下载最新 Release）
                    services.AddSingleton(
                        new RepositoryConfig
                        {
                            Owner = "abp-vnext-pro",
                            RepositoryId = "abp",
                            Description = "ABP VNext Pro 商业版主仓库（下载最新 Release）",
                            RepositoryType = RepositoryType.Business,
                            FrameworkName = "Aurora AbpPro Frameworks",
                            VersionSource = VersionSource.LatestRelease,
                        }
                    );
                    // 仓库 2：dotnetcore/CAP（版本由 abp-vnext-pro 的 *.targets 决定）
                    services.AddSingleton(
                        new RepositoryConfig
                        {
                            Owner = "dotnetcore",
                            RepositoryId = "CAP",
                            Description = "DotNetCore.CAP（版本来源：abp-vnext-pro *.targets）",
                            RepositoryType = RepositoryType.Source,
                            FrameworkName = "Aurora AbpPro CAP",
                            VersionSource = VersionSource.AbpVNextProTargets,
                        }
                    );
                    // 仓库 3：abpframework/abp（版本由 abp-vnext-pro 的 *.targets 决定）
                    services.AddSingleton(
                        new RepositoryConfig
                        {
                            Owner = "abpframework",
                            RepositoryId = "abp",
                            Description =
                                "ABP Framework 开源主仓库（版本来源：abp-vnext-pro *.targets）",
                            RepositoryType = RepositoryType.Source,
                            FrameworkName = "Aurora Abp Frameworks",
                            VersionSource = VersionSource.AbpVNextProTargets,
                        }
                    );
                    // 仓库 4：HangfireIO/Hangfire（版本由 abpframework 根目录 *.props 决定）
                    services.AddSingleton(
                        new RepositoryConfig
                        {
                            Owner = "HangfireIO",
                            RepositoryId = "Hangfire",
                            Description = "Hangfire（版本来源：abpframework *.props）",
                            RepositoryType = RepositoryType.Source,
                            FrameworkName = "Aurora Abp HangfireIO",
                            VersionSource = VersionSource.AbpFrameworkProps,
                        }
                    );

                    // 注册 ViewModel
                    services.AddSingleton<MainWindowViewModel>();
                    services.AddTransient<HomePageViewModel>();
                    // 框架页 VM 单例：保留下载进度与加载状态跨页面切换不丢失
                    services.AddSingleton<FrameworkPageViewModel>();
                    services.AddTransient<ProjectPageViewModel>();
                    // 配置页 VM 注册为单例：日志缓冲跨页面切换不丢失
                    services.AddSingleton<SettingsPageViewModel>();

                    // 注册页面（瞬时实例，便于切换）
                    services.AddTransient<HomePage>();
                    // 框架页单例，切换后仍复用同一控件实例，保留状态
                    services.AddSingleton<FrameworkPage>();
                    services.AddTransient<ProjectPage>();
                    services.AddTransient<SettingsPage>();

                    // 注册主窗口
                    services.AddSingleton<MainWindow>();
                }
            );
    }

    /// <summary>
    /// 注册 WPF / 任务 / 域级 全局异常捕获，统一写入 NLog
    /// </summary>
    private void RegisterGlobalExceptionHandlers()
    {
        // UI 线程未处理异常
        DispatcherUnhandledException += (_, args) =>
        {
            LogException(args.Exception, "DispatcherUnhandledException");
            args.Handled = true;
        };
        // 非 UI 线程未处理异常
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                LogException(ex, "AppDomain.UnhandledException");
            }
        };
        // Task 未观察异常
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogException(args.Exception, "UnobservedTaskException");
            args.SetObserved();
        };
    }

    /// <summary>
    /// 写入异常日志
    /// </summary>
    private static void LogException(Exception ex, string source)
    {
        var logger = NLog.LogManager.GetLogger("GlobalException");
        logger.Error(ex, "[{0}] 捕获未处理异常：{1}", source, ex.Message);
    }
}
