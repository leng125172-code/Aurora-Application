using AuroraStruct3D.Hubs;
using AuroraStruct3D.Projectors;
using AuroraStruct3D.Projectors.Dtos;
using Microsoft.AspNetCore.SignalR;
using Volo.Abp.DependencyInjection;

namespace AuroraStruct3D.Notifiers;

/// <summary>
/// 通过 SignalR Hub 推送投影仪条纹图下载进度通知的具体实现。
/// </summary>
[ExposeServices(typeof(IProjectorHubNotifier))]
public class SignalRProjectorHubNotifier : IProjectorHubNotifier, ISingletonDependency
{
    private readonly IHubContext<ProjectorHub, IProjectorHub> _hubContext;

    /// <summary>
    /// 初始化 <see cref="SignalRProjectorHubNotifier"/>。
    /// </summary>
    /// <param name="hubContext">ProjectorHub SignalR 上下文</param>
    public SignalRProjectorHubNotifier(IHubContext<ProjectorHub, IProjectorHub> hubContext)
    {
        _hubContext = hubContext;
    }

    /// <inheritdoc/>
    public Task NotifyFringeDownloadStatusChangedAsync(ProjectorFringeDownloadStatusDto status)
    {
        return _hubContext.Clients.All.ReceiveFringeDownloadStatusChangedAsync(status);
    }

    /// <inheritdoc/>
    public Task NotifyFringeDownloadProgressAsync(Guid projectorId, int progressPercent)
    {
        return _hubContext.Clients.All.ReceiveFringeDownloadProgressAsync(
            projectorId,
            progressPercent
        );
    }
}
