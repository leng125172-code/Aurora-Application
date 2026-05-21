using AuroraStruct3D.Cameras;
using AuroraStruct3D.Cameras.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Volo.Abp.Application.Dtos;
using Volo.Abp.AspNetCore.SignalR;

namespace AuroraStruct3D.Hubs;

/// <summary>
/// 相机实时推流 SignalR Hub。
/// 允许匿名访问；客户端连接时推送全部相机的当前状态快照。
/// 帧数据由 CameraPreviewService 后台服务直接推送，不经过此 Hub 方法。
/// </summary>
[AllowAnonymous]
[DisableAutoHubMap]
public class CameraHub : AbpHub<ICameraHub>
{
    private readonly ICameraDeviceAppService _cameraDeviceAppService;

    public CameraHub(ICameraDeviceAppService cameraDeviceAppService)
    {
        _cameraDeviceAppService = cameraDeviceAppService;
    }

    /// <summary>客户端连接时，立即推送全部相机的当前状态快照</summary>
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();

        PagedResultDto<CameraDeviceDto> result = await _cameraDeviceAppService.GetListAsync(
            new GetCameraListDto { MaxResultCount = 100 }
        );

        foreach (CameraDeviceDto camera in result.Items)
        {
            await Clients.Caller.ReceiveCameraStateAsync(
                camera.Id.ToString(),
                camera.Status.ToString()
            );
        }
    }

    /// <summary>
    /// 向所有客户端广播相机状态变更（供后台服务调用）
    /// </summary>
    public async Task BroadcastCameraStateAsync(Guid cameraId, string status)
    {
        await Clients.All.ReceiveCameraStateAsync(cameraId.ToString(), status);
    }
}
