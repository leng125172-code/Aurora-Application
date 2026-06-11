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

    /// <summary>
    /// 客户端在断线重连或页面刷新后调用：在 30s 宽限期内续约指定相机的预览会话，
    /// 将会话的 ConnectionId 重新指向当前连接，避免预览被宽限期到期时误回收。
    /// </summary>
    /// <param name="cameraId">相机设备 ID</param>
    /// <returns>true 表示成功取消挂起停止；false 表示该相机当前不在宽限期内</returns>
    public Task<bool> ReattachPreviewAsync(string cameraId)
    {
        if (!Guid.TryParse(cameraId, out Guid id))
        {
            return Task.FromResult(false);
        }
        bool canceled = _cameraPreviewService.ReattachPreviewAsync(id, Context.ConnectionId);
        return Task.FromResult(canceled);
    }

    /// <summary>
    /// 客户端心跳探针：前端定期调用以维持连接活跃，防止长时间等待耗时操作时连接超时断开。
    /// </summary>
    /// <returns>服务器 UTC 时间戳（毫秒），供前端计算往返延迟</returns>
    public Task<long> PingAsync()
    {
        return Task.FromResult(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    /// <summary>
    /// 加入 Step6 在线扫描项目分组。
    /// </summary>
    public async Task JoinCalibScanGroupAsync(string calibProjectId)
    {
        if (!Guid.TryParse(calibProjectId, out Guid id))
        {
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, BuildCalibScanGroupName(id));
    }

    /// <summary>
    /// 退出 Step6 在线扫描项目分组。
    /// </summary>
    public async Task LeaveCalibScanGroupAsync(string calibProjectId)
    {
        if (!Guid.TryParse(calibProjectId, out Guid id))
        {
            return;
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, BuildCalibScanGroupName(id));
    }

    /// <summary>
    /// 加入 Step7 点云生成项目分组。
    /// </summary>
    public async Task JoinPointCloudGroupAsync(string calibProjectId)
    {
        if (!Guid.TryParse(calibProjectId, out Guid id))
        {
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, BuildPointCloudGroupName(id));
    }

    /// <summary>
    /// 退出 Step7 点云生成项目分组。
    /// </summary>
    public async Task LeavePointCloudGroupAsync(string calibProjectId)
    {
        if (!Guid.TryParse(calibProjectId, out Guid id))
        {
            return;
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, BuildPointCloudGroupName(id));
    }

    private static string BuildCalibScanGroupName(Guid calibProjectId)
    {
        return $"calib-scan:{calibProjectId:N}";
    }

    private static string BuildPointCloudGroupName(Guid calibProjectId)
    {
        return $"calib-point-cloud:{calibProjectId:N}";
    }
}
