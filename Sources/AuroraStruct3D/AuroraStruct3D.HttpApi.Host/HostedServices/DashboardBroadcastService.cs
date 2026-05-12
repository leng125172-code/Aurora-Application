using AuroraStruct3D.Hubs;
using AuroraStruct3D.Services;
using DotNetCore.CAP.Monitoring;
using DotNetCore.CAP.Persistence;
using Hangfire;
using Hangfire.Storage.Monitoring;
using Microsoft.AspNetCore.SignalR;

namespace AuroraStruct3D.HostedServices;

/// <summary>
/// 仪表盘统计广播后台服务。
/// 每隔 5 秒采集一次 CAP 和 Hangfire 的统计数据，
/// 通过 SignalR <see cref="DashboardHub"/> 实时推送给所有已连接的客户端。
/// </summary>
public class DashboardBroadcastService : BackgroundService
{
    private static readonly TimeSpan _interval = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<DashboardHub> _hubContext;
    private readonly ILogger<DashboardBroadcastService> _logger;
    private readonly SystemMetricsCollector _metrics;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="scopeFactory">服务作用域工厂，用于解析作用域服务（如 CAP IDataStorage）</param>
    /// <param name="hubContext">DashboardHub 上下文，用于向客户端推送消息</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="metrics">系统指标采集器（单例）</param>
    public DashboardBroadcastService(
        IServiceScopeFactory scopeFactory,
        IHubContext<DashboardHub> hubContext,
        ILogger<DashboardBroadcastService> logger,
        SystemMetricsCollector metrics
    )
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _logger = logger;
        _metrics = metrics;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 延迟 3 秒后再开始广播，等待应用完全启动
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await BroadcastAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // 应用正在关闭，退出循环
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "广播仪表盘统计数据时发生错误，将在下次间隔后重试。");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    /// <summary>
    /// 采集 CAP 和 Hangfire 统计数据并广播给所有客户端
    /// </summary>
    private async Task BroadcastAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        // ── 采集 CAP 统计数据 ──────────────────────────────────────────────
        object? capStats = null;
        try
        {
            var dataStorage = scope.ServiceProvider.GetRequiredService<IDataStorage>();
            var monitoringApi = dataStorage.GetMonitoringApi();
            var statistics = await monitoringApi.GetStatisticsAsync();
            capStats = new
            {
                publishSucceeded = statistics.PublishedSucceeded,
                publishFailed = statistics.PublishedFailed,
                consumeSucceeded = statistics.ReceivedSucceeded,
                consumeFailed = statistics.ReceivedFailed,
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "采集 CAP 统计数据失败。");
        }

        // ── 采集 Hangfire 统计数据 ─────────────────────────────────────────
        object? hangfireStats = null;
        try
        {
            var jobStorage = scope.ServiceProvider.GetRequiredService<JobStorage>();
            var monitoringApi = jobStorage.GetMonitoringApi();
            var statistics = monitoringApi.GetStatistics();
            hangfireStats = new
            {
                enqueued = statistics.Enqueued,
                scheduled = statistics.Scheduled,
                processing = statistics.Processing,
                succeeded = statistics.Succeeded,
                failed = statistics.Failed,
                deleted = statistics.Deleted,
                recurring = statistics.Recurring,
                servers = statistics.Servers,
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "采集 Hangfire 统计数据失败。");
        }

        // ── 推送给所有已连接的客户端 ──────────────────────────────────────
        if (capStats is not null)
        {
            await _hubContext.Clients.All.SendAsync("ReceiveCapStats", capStats, cancellationToken);
        }

        if (hangfireStats is not null)
        {
            await _hubContext.Clients.All.SendAsync(
                "ReceiveHangfireStats",
                hangfireStats,
                cancellationToken
            );
        }

        // ── 采集并推送系统指标 ────────────────────────────────────────────
        SystemMetricsSnapshot snapshot = _metrics.Collect();
        await _hubContext.Clients.All.SendAsync(
            "ReceiveSystemMetrics",
            new
            {
                cpuPercent = snapshot.CpuPercent,
                memoryPercent = snapshot.MemoryPercent,
                memoryUsedBytes = snapshot.MemoryUsedBytes,
                memoryTotalBytes = snapshot.MemoryTotalBytes,
                networkSendRate = snapshot.NetworkSendRateBytes,
                networkReceiveRate = snapshot.NetworkReceiveRateBytes,
                networkTotalSent = snapshot.NetworkTotalSentBytes,
                networkTotalReceived = snapshot.NetworkTotalReceivedBytes,
                npuPercent = snapshot.NpuPercent,
                gpuPercent = snapshot.GpuPercent,
            },
            cancellationToken
        );
    }
}
