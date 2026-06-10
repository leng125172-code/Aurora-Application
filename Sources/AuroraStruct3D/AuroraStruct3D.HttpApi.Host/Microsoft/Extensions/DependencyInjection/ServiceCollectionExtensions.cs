using Hangfire.PostgreSql;
using Lion.AbpPro.CAP;
using Medallion.Threading;
using Medallion.Threading.Redis;
using Microsoft.AspNetCore.SignalR.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Savorboard.CAP.InMemoryMessageQueue;
using Volo.Abp.BlobStoring;

#pragma warning disable CS0618 // Type or member is obsolete

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册Redis缓存
    /// </summary>
    public static IServiceCollection AddAbpProRedis(
        this IServiceCollection service,
        Action<AbpDistributedCacheOptions> configureOptions = null
    )
    {
        var configuration = service.GetConfiguration();
        var redisEnabled = configuration.GetValue<bool>("Redis:IsEnabled");
        if (!redisEnabled)
            return service;

        if (configureOptions != null)
        {
            service.Configure(configureOptions);
        }
        else
        {
            service.Configure<AbpDistributedCacheOptions>(options =>
            {
                options.KeyPrefix = "AbpPro:";
            });
        }

        var redis = ConnectionMultiplexer.Connect(
            configuration.GetValue<string>("Redis:Configuration")
        );
        service
            .AddDataProtection()
            .PersistKeysToStackExchangeRedis(redis, "AbpPro-Protection-Keys");
        return service;
    }

    /// <summary>
    /// 注册redis分布式锁
    /// </summary>
    public static IServiceCollection AddAbpProRedisDistributedLocking(
        this IServiceCollection service
    )
    {
        var configuration = service.GetConfiguration();
        var redisEnabled = configuration.GetValue<bool>("Redis:IsEnabled");
        if (!redisEnabled)
            return service;

        var connectionString = configuration.GetValue<string>("Redis:Configuration");
        service.AddSingleton<IDistributedLockProvider>(sp =>
        {
            var connection = ConnectionMultiplexer.Connect(connectionString);
            return new RedisDistributedSynchronizationProvider(connection.GetDatabase());
        });
        return service;
    }

    /// <summary>
    /// 注册Identity
    /// </summary>
    public static IServiceCollection AddAbpProIdentity(this IServiceCollection service)
    {
        service.Configure<IdentityOptions>(options =>
        {
            options.Lockout = new LockoutOptions() { AllowedForNewUsers = false };
        });
        return service;
    }

    /// <summary>
    /// 注册SignalR
    /// </summary>
    public static IServiceCollection AddAbpProSignalR(
        this IServiceCollection service,
        Action<RedisOptions> redisOptions = null
    )
    {
        var configuration = service.GetConfiguration();
        var redisEnabled = configuration.GetValue<bool>("Redis:IsEnabled");
        if (redisEnabled)
        {
            if (redisOptions != null)
            {
                service
                    .AddSignalR()
                    .AddStackExchangeRedis(
                        service.GetConfiguration().GetValue<string>("Redis:Configuration"),
                        redisOptions
                    );
            }
            else
            {
                service
                    .AddSignalR()
                    .AddStackExchangeRedis(
                        service.GetConfiguration().GetValue<string>("Redis:Configuration"),
                        options =>
                        {
                            options.Configuration.ChannelPrefix = "Lion.AbpPro";
                        }
                    );
            }
        }
        else
        {
            service.AddSignalR();
        }

        return service;
    }

    /// <summary>
    /// 注册基于FileSystem的blob设置
    /// </summary>
    /// <param name="service">服务集合</param>
    /// <param name="configuration">应用配置，用于读取 BlobStoring 各容器的 BasePath</param>
    public static IServiceCollection AddAbpProBlobStorageFileSystem(
        this IServiceCollection service,
        IConfiguration configuration
    )
    {
        var defaultBasePath = ResolveBlobBasePath(
            configuration,
            "BlobStoring:Default:BasePath",
            "files",
            "default"
        );
        var productModelsBasePath = ResolveBlobBasePath(
            configuration,
            "BlobStoring:ProductModels:BasePath",
            "files",
            "product-models"
        );
        var aiModelsBasePath = ResolveBlobBasePath(
            configuration,
            "BlobStoring:AiModels:BasePath",
            "files",
            "ai-models"
        );
        var calibPhotosBasePath = ResolveBlobBasePath(
            configuration,
            "BlobStoring:CalibPhotos:BasePath",
            "files",
            "calib-photos"
        );

        service.Configure<AbpBlobStoringOptions>(options =>
        {
            options.Containers.ConfigureDefault(container =>
            {
                container.UseFileSystem(fileSystem =>
                {
                    fileSystem.BasePath = defaultBasePath;
                });
            });

            // 三维数模专属容器，使用本地文件系统存储
            options.Containers.Configure<AuroraStruct3D.ProductModels.ProductModelBlobContainer>(
                container =>
                {
                    container.UseFileSystem(fileSystem =>
                    {
                        fileSystem.BasePath = productModelsBasePath;
                    });
                }
            );

            // AI 模型专属容器，使用本地文件系统存储
            options.Containers.Configure<AuroraStruct3D.AI.AiModelBlobContainer>(container =>
            {
                container.UseFileSystem(fileSystem =>
                {
                    fileSystem.BasePath = aiModelsBasePath;
                });
            });

            options.Containers.Configure<AuroraStruct3D.Calibration.CalibPhotoBlobContainer>(
                container =>
                {
                    container.UseFileSystem(fileSystem =>
                    {
                        fileSystem.BasePath = calibPhotosBasePath;
                    });
                }
            );
        });
        return service;
    }

    private static string ResolveBlobBasePath(
        IConfiguration configuration,
        string configurationKey,
        params string[] fallbackSegments
    )
    {
        string? configuredBasePath = configuration[configurationKey];
        if (string.IsNullOrWhiteSpace(configuredBasePath))
        {
            return BuildFallbackBlobBasePath(fallbackSegments);
        }

        if (!OperatingSystem.IsWindows() && LooksLikeWindowsAbsolutePath(configuredBasePath))
        {
            throw new InvalidOperationException(
                $"Blob 存储配置项 {configurationKey} 使用了 Windows 绝对路径 {configuredBasePath}，请改为 Linux 绝对路径或相对程序目录的路径。"
            );
        }

        return Path.IsPathRooted(configuredBasePath)
            ? Path.GetFullPath(configuredBasePath)
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredBasePath));
    }

    private static string BuildFallbackBlobBasePath(params string[] segments)
    {
        string path = AppContext.BaseDirectory;
        foreach (string segment in segments)
        {
            path = Path.Combine(path, segment);
        }

        return Path.GetFullPath(path);
    }

    private static bool LooksLikeWindowsAbsolutePath(string path)
    {
        return path.Length >= 3
            && char.IsLetter(path[0])
            && path[1] == ':'
            && (path[2] == '\\' || path[2] == '/');
    }

    /// <summary>
    /// 注册cap —— 纯PG + 无RabbitMQ
    /// </summary>
    public static IServiceCollection AddAbpProCap(this IServiceCollection service)
    {
        var configuration = service.GetConfiguration();
        service.AddAbpCap(capOptions =>
        {
            // PostgreSQL 存储
            capOptions.UsePostgreSql(configuration.GetConnectionString("Default"));

            // 不用 RabbitMQ！用内存传输（单机直接跑）
            capOptions.UseInMemoryMessageQueue();

            capOptions.FailedRetryCount = 0;

            // CAP 面板
            capOptions.UseDashboard(options =>
            {
                options.AuthorizationPolicy = "AbpProCapPermissions.CapManagement.Cap";
            });
        });
        return service;
    }

    /// <summary>
    /// 注册hangfire —— 纯PostgreSQL版
    /// </summary>
    public static IServiceCollection AddAbpProHangfire(this IServiceCollection service)
    {
        var configuration = service.GetConfiguration();

        service.Configure<AbpBackgroundJobOptions>(options =>
        {
            options.IsJobExecutionEnabled = true;
        });

        service.AddHangfire(config =>
        {
            // 使用新版 API 注册 PostgreSQL 存储
            config.UsePostgreSqlStorage(options =>
            {
                options.UseNpgsqlConnection(configuration.GetConnectionString("Default"));
            });

            // 重试策略
            config.UseFilter(
                new AutomaticRetryAttribute
                {
                    Attempts = 3,
                    DelaysInSeconds = new[] { 10, 60, 180 },
                }
            );
        });

        service.AddHangfireServer();
        return service;
    }
}
