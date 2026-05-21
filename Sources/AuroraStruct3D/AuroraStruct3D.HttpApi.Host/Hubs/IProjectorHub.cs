using AuroraStruct3D.Projectors.Dtos;

namespace AuroraStruct3D.Hubs;

/// <summary>
/// 投影机状态 SignalR Hub 的客户端推送接口
/// </summary>
public interface IProjectorHub
{
    /// <summary>推送投影机状态快照给客户端</summary>
    Task ReceiveProjectorStateAsync(ProjectorDeviceDto projector);

    /// <summary>推送投影机连接状态变更事件给客户端</summary>
    Task ReceiveProjectorConnectionChangedAsync(Guid projectorDeviceId, string status);

    /// <summary>推送投影机 LED 状态变更事件给客户端</summary>
    Task ReceiveProjectorLedChangedAsync(Guid projectorDeviceId, string ledStatus);
}
