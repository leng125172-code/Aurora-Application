using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AuroraStruct3D.OpenCV.Registry;

/// <summary>
/// 算子注册表启动初始化器。
/// 在宿主应用启动时（<see cref="StartAsync"/>）自动扫描所有注册程序集，
/// 将算子元数据和端口描述写入 Redis，供后续请求直接读缓存。
/// <para>
/// 初始化失败只记录错误日志，不阻止应用启动，保证服务可用性。
/// </para>
/// </summary>
internal sealed class OperatorRegistryInitializer : IHostedService
{
    private readonly OperatorRegistry _registry;
    private readonly ILogger<OperatorRegistryInitializer> _logger;

    public OperatorRegistryInitializer(
        OperatorRegistry registry,
        ILogger<OperatorRegistryInitializer> logger
    )
    {
        _registry = registry;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _registry.InitializeAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // 不抛出异常，避免注册表初始化失败导致整个应用崩溃
            _logger.LogError(ex, "算子注册表初始化失败，算子列表查询将返回空结果直至下次重启");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
