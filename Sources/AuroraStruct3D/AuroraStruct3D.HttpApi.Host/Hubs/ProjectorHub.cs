using AuroraStruct3D.Projectors;
using AuroraStruct3D.Projectors.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Volo.Abp.AspNetCore.SignalR;

namespace AuroraStruct3D.Hubs;

/// <summary>
/// 投影机实时状态推送 SignalR Hub。
/// 允许匿名访问，客户端连接后立即收到全部投影机的当前状态快照。
/// </summary>
[AllowAnonymous]
[DisableAutoHubMap]
public class ProjectorHub : AbpHub<IProjectorHub>
{
    private readonly IProjectorDeviceAppService _projectorDeviceAppService;

    public ProjectorHub(IProjectorDeviceAppService projectorDeviceAppService)
    {
        _projectorDeviceAppService = projectorDeviceAppService;
    }

    /// <summary>客户端连接时，立即推送全部投影机状态快照</summary>
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();

        // 查询全部投影机并逐一推送给当前连接的客户端
        Volo.Abp.Application.Dtos.PagedResultDto<ProjectorDeviceDto> result =
            await _projectorDeviceAppService.GetListAsync(
                new GetProjectorListDto { MaxResultCount = 100 }
            );

        foreach (ProjectorDeviceDto projector in result.Items)
        {
            await Clients.Caller.ReceiveProjectorStateAsync(projector);
        }
    }

    /// <summary>
    /// 向所有客户端广播某台投影机的最新状态（供 AppService 层注入 Hub 后调用）
    /// </summary>
    public async Task BroadcastProjectorStateAsync(ProjectorDeviceDto projector)
    {
        await Clients.All.ReceiveProjectorStateAsync(projector);
    }

    /// <summary>
    /// 向所有客户端广播投影机连接状态变更
    /// </summary>
    public async Task BroadcastConnectionChangedAsync(Guid projectorDeviceId, string status)
    {
        await Clients.All.ReceiveProjectorConnectionChangedAsync(projectorDeviceId, status);
    }

    /// <summary>
    /// 向所有客户端广播投影机 LED 状态变更
    /// </summary>
    public async Task BroadcastLedChangedAsync(Guid projectorDeviceId, string ledStatus)
    {
        await Clients.All.ReceiveProjectorLedChangedAsync(projectorDeviceId, ledStatus);
    }
}
