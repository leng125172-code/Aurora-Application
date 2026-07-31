using AuroraStruct3D.Hubs;
using AuroraStruct3D.Plcs;
using Microsoft.AspNetCore.SignalR;

namespace AuroraStruct3D.Notifiers;

public sealed class SignalRPlcRealtimeNotifier : IPlcRealtimeNotifier
{
    private readonly IHubContext<PlcHub, IPlcHub> _hub;

    public SignalRPlcRealtimeNotifier(IHubContext<PlcHub, IPlcHub> hub) => _hub = hub;

    public Task ValueChangedAsync(string connectionId, PlcTagValueDto value) =>
        _hub.Clients.Client(connectionId).ValueChanged(value);

    public Task ConnectionStateChangedAsync(
        Guid deviceId,
        PlcConnectionStatus status,
        string? error
    ) => _hub.Clients.All.ConnectionStateChanged(deviceId, status, error);
}
