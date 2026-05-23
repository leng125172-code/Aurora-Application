using AuroraStruct3D.Cameras;
using AuroraStruct3D.Cameras.Dtos;
using AuroraStruct3D.Streaming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
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
    private readonly CameraPreviewService _cameraPreviewService;
    private readonly ILogger<CameraHub> _logger;

    public CameraHub(
        ICameraDeviceAppService cameraDeviceAppService,
        CameraPreviewService cameraPreviewService,
        ILogger<CameraHub> logger
    )
    {
        _cameraDeviceAppService = cameraDeviceAppService;
        _cameraPreviewService = cameraPreviewService;
        _logger = logger;
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
                new CameraStateDto
                {
                    CameraId = camera.Id,
                    Status = (int)camera.Status,
                    StatusText = camera.Status.ToString(),
                    IsCapturing = camera.Status == CameraStatus.Capturing,
                    IsXmlLoaded = false,
                }
            );
        }
    }

    /// <summary>客户端断开时，释放由该连接启动的相机预览</summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try
        {
            await _cameraPreviewService.StopPreviewByConnectionAsync(Context.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "SignalR 连接 {ConnectionId} 断开时清理相机预览失败",
                Context.ConnectionId
            );
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// 向所有客户端广播相机状态变更（供后台服务调用）
    /// </summary>
    public async Task BroadcastCameraStateAsync(CameraStateDto state)
    {
        await Clients.All.ReceiveCameraStateAsync(state);
    }
}
