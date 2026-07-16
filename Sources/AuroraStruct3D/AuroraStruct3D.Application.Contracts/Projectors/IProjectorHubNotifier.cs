using AuroraStruct3D.Projectors.Dtos;

namespace AuroraStruct3D.Projectors;

/// <summary>
/// 投影仪 SignalR 通知器接口。
/// 由 HttpApi.Host 层通过 <see cref="AuroraStruct3D.Hubs.ProjectorHub"/> 实现，
/// 用于向前端实时推送条纹图下载进度事件。
/// </summary>
public interface IProjectorHubNotifier
{
    /// <summary>
    /// 推送条纹图下载状态变更至前端。
    /// </summary>
    Task NotifyFringeDownloadStatusChangedAsync(ProjectorFringeDownloadStatusDto status);

    /// <summary>
    /// 推送条纹图下载进度至前端。
    /// </summary>
    /// <param name="projectorId">投影仪设备 ID</param>
    /// <param name="progressPercent">进度百分比（0~100）</param>
    Task NotifyFringeDownloadProgressAsync(Guid projectorId, int progressPercent);
}
