using AuroraStruct3D.Hubs;
using AuroraStruct3D.Sessions;
using Microsoft.AspNetCore.SignalR;
using Volo.Abp.DependencyInjection;

namespace AuroraStruct3D.Sessions;

/// <summary>
/// 设备会话广播器：订阅 <see cref="IDeviceOperationSessionManager.SessionChanged"/> 事件，
/// 通过 SignalR 将变更实时推送给所有已连接客户端。
/// 单例，在 Module.ConfigureServices 中手动注册以确保启动时即可订阅事件。
/// </summary>
public sealed class DeviceSessionBroadcaster : ISingletonDependency
{
    private readonly IHubContext<DeviceStateHub, IDeviceStateHub> _hubContext;

    public DeviceSessionBroadcaster(
        IDeviceOperationSessionManager sessionManager,
        IHubContext<DeviceStateHub, IDeviceStateHub> hubContext
    )
    {
        _hubContext = hubContext;

        // 订阅会话变更事件，将变更广播给所有 SignalR 客户端
        sessionManager.SessionChanged += OnSessionChangedAsync;
    }

    private void OnSessionChangedAsync(DeviceSessionChangedDto changed)
    {
        // 使用 Fire-and-Forget（Hub 推送无需 await），异常由 SignalR 内部处理
        _ = _hubContext.Clients.All.ReceiveDeviceSessionChangedAsync(changed);
    }
}
