using Microsoft.AspNetCore.Authorization;
using Volo.Abp.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR;
using AuroraStruct3D.Plcs;

namespace AuroraStruct3D.Hubs;

[Authorize]
[DisableAutoHubMap] // 由 Host 模块显式映射，避免 ABP 自动映射产生重复 negotiate 端点
public class PlcHub : AbpHub<IPlcHub>
{
    private readonly IPlcSubscriptionTracker _tracker;
    private readonly IPlcDeviceAppService _plcs;

    public PlcHub(IPlcSubscriptionTracker tracker, IPlcDeviceAppService plcs)
    {
        _tracker = tracker;
        _plcs = plcs;
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        foreach (PlcTrackedSubscription subscription in _tracker.TakeByConnection(Context.ConnectionId))
        {
            try
            {
                await _plcs.UnsubscribeAsync(
                    subscription.DeviceId,
                    subscription.SubscriptionId
                );
            }
            catch { }
        }
        await base.OnDisconnectedAsync(exception);
    }
}
