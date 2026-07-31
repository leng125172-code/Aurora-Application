using Microsoft.AspNetCore.Authorization;
using Volo.Abp.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR;
using AuroraStruct3D.Plcs;

namespace AuroraStruct3D.Hubs;

[Authorize]
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
