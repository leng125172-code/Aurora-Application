using AuroraStruct3D.DeviceState;
using AuroraStruct3D.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace AuroraStruct3D.HostedServices;

/// <summary>
/// 设备状态实时推送后台服务。
/// 订阅 <see cref="IDeviceStateManager"/> 的状态和模式变更事件，
/// 通过 SignalR <see cref="DeviceStateHub"/> 广播给所有已连接的客户端。
/// </summary>
public class DeviceStatePushService : IHostedService
{
    private readonly IDeviceStateManager _deviceStateManager;
    private readonly IHubContext<DeviceStateHub, IDeviceStateHub> _hubContext;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DeviceStatePushService> _logger;

    /// <summary>构造函数</summary>
    public DeviceStatePushService(
        IDeviceStateManager deviceStateManager,
        IHubContext<DeviceStateHub, IDeviceStateHub> hubContext,
        IServiceScopeFactory scopeFactory,
        ILogger<DeviceStatePushService> logger
    )
    {
        _deviceStateManager = deviceStateManager;
        _hubContext = hubContext;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // 订阅状态变更事件
        _deviceStateManager.StatusChanged += OnStatusChanged;
        // 订阅模式变更事件
        _deviceStateManager.ModeChanged += OnModeChanged;

        _logger.LogInformation("DeviceStatePushService 已启动，开始监听设备状态变更。");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        // 取消订阅，防止内存泄漏
        _deviceStateManager.StatusChanged -= OnStatusChanged;
        _deviceStateManager.ModeChanged -= OnModeChanged;

        _logger.LogInformation("DeviceStatePushService 已停止。");
        return Task.CompletedTask;
    }

    // ─── 事件处理 ─────────────────────────────────────────────────────────────

    /// <summary>设备状态变更时触发广播</summary>
    private void OnStatusChanged(
        DeviceStatus oldStatus,
        DeviceStatus newStatus,
        AuroraStruct3D.DeviceState.StateChangeContext context
    )
    {
        // 抑制 ExecutionContext 流传播，防止 HTTP 请求的 ABP UoW（已 disposed）传入广播 Task
        using (ExecutionContext.SuppressFlow())
            _ = BroadcastStateAsync();
    }

    /// <summary>运行模式变更时触发广播</summary>
    private void OnModeChanged(
        DeviceRunMode oldMode,
        DeviceRunMode newMode,
        AuroraStruct3D.DeviceState.StateChangeContext context
    )
    {
        // 抑制 ExecutionContext 流传播，防止 HTTP 请求的 ABP UoW（已 disposed）传入广播 Task
        using (ExecutionContext.SuppressFlow())
            _ = BroadcastStateAsync();
    }

    // ─── 广播逻辑 ─────────────────────────────────────────────────────────────

    /// <summary>采集当前快照并广播给所有客户端</summary>
    private async Task BroadcastStateAsync()
    {
        try
        {
            DeviceStateDto stateDto = BuildCurrentStateDto();
            await _hubContext.Clients.All.ReceiveDeviceStateAsync(stateDto);

            // 同步广播当前故障（需要数据库访问，使用作用域）
            DeviceFaultDto? faultDto = await GetCurrentFaultDtoAsync();
            await _hubContext.Clients.All.ReceiveDeviceFaultAsync(faultDto);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "广播设备状态变更时发生错误。");
        }
    }

    /// <summary>构建当前设备状态 DTO</summary>
    private DeviceStateDto BuildCurrentStateDto() =>
        new DeviceStateDto
        {
            Status = _deviceStateManager.Status,
            RunMode = _deviceStateManager.RunMode,
            CurrentFaultId = _deviceStateManager.CurrentFaultId,
            CurrentFaultLevel = _deviceStateManager.CurrentFaultLevel,
            CurrentFaultCode = _deviceStateManager.CurrentFaultCode,
            IsInTransition = _deviceStateManager.IsInTransition,
            CanAcceptProductionCommand = _deviceStateManager.CanAcceptProductionCommand,
            CanSwitchMode = _deviceStateManager.CanSwitchMode,
        };

    /// <summary>从数据库获取当前活跃故障 DTO（使用作用域防止跨请求泄漏）</summary>
    private async Task<DeviceFaultDto?> GetCurrentFaultDtoAsync()
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IDeviceFaultRepository repo =
            scope.ServiceProvider.GetRequiredService<IDeviceFaultRepository>();

        List<DeviceFault> unresolved = await repo.GetUnresolvedListAsync();
        DeviceFault? fault = unresolved.FirstOrDefault();
        if (fault is null)
            return null;

        return new DeviceFaultDto
        {
            Id = fault.Id,
            OccurredAt = fault.OccurredAt,
            FaultLevel = fault.FaultLevel,
            FaultCode = fault.FaultCode,
            FaultMessage = fault.FaultMessage,
            FaultReason = fault.FaultReason,
            IsResolved = fault.IsResolved,
            IsAutoRecovered = fault.IsAutoRecovered,
            ResolverId = fault.ResolverId,
            ResolverName = fault.ResolverName,
            ResolvedAt = fault.ResolvedAt,
            ResolutionDescription = fault.ResolutionDescription,
            DurationMs = fault.DurationMs,
            CausedModeSwitch = fault.CausedModeSwitch,
            SwitchedToMode = fault.SwitchedToMode,
            StateLogId = fault.StateLogId,
            Remark = fault.Remark,
            CreationTime = fault.CreationTime,
        };
    }
}
