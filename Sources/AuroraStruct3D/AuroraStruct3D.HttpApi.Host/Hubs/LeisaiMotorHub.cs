using Microsoft.AspNetCore.Authorization;
using AuroraStruct3D.Realtime;
using Volo.Abp.AspNetCore.SignalR;

namespace AuroraStruct3D.Hubs;

/// <summary>
/// 雷赛 iCL-RS 电机操作台实时数据推送 SignalR Hub。
/// 允许匿名访问；连接后被动接收 State 推送，无客户端调用方法。
/// </summary>
[AllowAnonymous]
[DisableAutoHubMap]
public class LeisaiMotorHub(RealtimeSubscriberTracker subscribers) : AbpHub<ILeisaiMotorHub>
{
    public override async Task OnConnectedAsync()
    {
        subscribers.Connected(RealtimeSubscriberTracker.LeisaiMotor);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        subscribers.Disconnected(RealtimeSubscriberTracker.LeisaiMotor);
        await base.OnDisconnectedAsync(exception);
    }
}
