using Microsoft.AspNetCore.SignalR;

namespace Aurora.Hmi.Host;

/// <summary>Pushes station snapshots to WinUI, Avalonia, and browser clients.</summary>
public sealed class StationHub : Hub
{
    /// <summary>Subscribes the connection to one station.</summary>
    public Task JoinStation(string stationId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupName(stationId));

    internal static string GroupName(string stationId) => $"station:{stationId}";
}
