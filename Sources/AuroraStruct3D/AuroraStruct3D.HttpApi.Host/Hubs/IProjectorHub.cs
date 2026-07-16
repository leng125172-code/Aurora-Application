using AuroraStruct3D.Projectors.Dtos;

namespace AuroraStruct3D.Hubs;

/// <summary>
/// 投影仪状态 SignalR Hub 的客户端推送接口
/// </summary>
public interface IProjectorHub
{
    /// <summary>推送投影仪状态快照给客户端</summary>
    Task ReceiveProjectorStateAsync(ProjectorDeviceDto projector);

    /// <summary>推送投影仪连接状态变更事件给客户端</summary>
    Task ReceiveProjectorConnectionChangedAsync(Guid projectorDeviceId, string status);

    /// <summary>推送投影仪 LED 状态变更事件给客户端</summary>
    Task ReceiveProjectorLedChangedAsync(Guid projectorDeviceId, string ledStatus);

    /// <summary>推送条纹图案下载状态变更给客户端</summary>
    Task ReceiveFringeDownloadStatusChangedAsync(ProjectorFringeDownloadStatusDto status);

    /// <summary>推送条纹图案下载进度给客户端（0~100）</summary>
    Task ReceiveFringeDownloadProgressAsync(Guid projectorDeviceId, int progress);
}
