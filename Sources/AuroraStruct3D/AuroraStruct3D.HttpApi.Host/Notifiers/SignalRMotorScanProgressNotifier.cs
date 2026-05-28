using AuroraStruct3D.Hubs;
using AuroraStruct3D.Motors;
using AuroraStruct3D.Motors.Dtos;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.DependencyInjection;

namespace AuroraStruct3D.Notifiers;

/// <summary>
/// 通过 SignalR Hub 推送电机扫描进度的具体实现。
/// 使用 ExposeServices 显式替换默认的 <see cref="NullMotorScanProgressNotifier"/>。
/// </summary>
[ExposeServices(typeof(IMotorScanProgressNotifier))]
public class SignalRMotorScanProgressNotifier : IMotorScanProgressNotifier, ISingletonDependency
{
    private readonly IHubContext<MotorScanHub, IMotorScanHub> _hubContext;

    public SignalRMotorScanProgressNotifier(IHubContext<MotorScanHub, IMotorScanHub> hubContext)
    {
        _hubContext = hubContext;
    }

    /// <inheritdoc/>
    public Task NotifyAsync(MotorScanProgressDto progress)
    {
        // 广播给所有已连接客户端，前端按需展示
        return _hubContext.Clients.All.ReceiveMotorScanProgressAsync(progress);
    }
}
