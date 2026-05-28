using AuroraStruct3D.Cameras.Dtos;

namespace AuroraStruct3D.Cameras;

/// <summary>
/// 相机实时推流服务接口（由 HttpApi.Host 中的 CameraPreviewService 实现）
/// </summary>
public interface ICameraStreamingService
{
    /// <summary>
    /// 开始对指定相机进行实时推流（SignalR + 可选 RTP/MJPEG UDP）
    /// </summary>
    /// <param name="cameraId">相机设备 ID</param>
    /// <param name="connectionId">SignalR 连接 ID（精准推帧，null 表示全组播）</param>
    /// <param name="enableRtp">是否同时开启 RTP/MJPEG UDP 流</param>
    /// <param name="imageRotationAngle">图像顺时针旋转角度（度）</param>
    /// <param name="clientSessionId">调用方浏览器标签页 UUID（用于设备独占会话）；null 表示系统内部调用</param>
    /// <param name="forceSession">是否强制接管设备独占会话（踢出当前占用者）</param>
    Task StartPreviewAsync(
        Guid cameraId,
        string? connectionId,
        bool enableRtp,
        int imageRotationAngle,
        string? clientSessionId = null,
        bool forceSession = false
    );

    /// <summary>
    /// 更新正在预览中的软件图像旋转角度；未预览时直接忽略
    /// </summary>
    /// <param name="cameraId">相机设备 ID</param>
    /// <param name="imageRotationAngle">图像顺时针旋转角度（度）</param>
    Task UpdatePreviewRotationAsync(Guid cameraId, int imageRotationAngle);

    /// <summary>
    /// 停止指定相机的实时推流
    /// </summary>
    Task StopPreviewAsync(Guid cameraId);

    /// <summary>
    /// 获取指定相机的 RTP/MJPEG 推流端点信息；相机未在推流时返回 null
    /// </summary>
    CameraRtpEndpointDto? GetRtpEndpoint(Guid cameraId);

    /// <summary>
    /// 通过 SignalR 向所有客户端推送一批 GenICam 节点的增量变更
    /// </summary>
    /// <param name="cameraId">相机设备 ID</param>
    /// <param name="changes">变更节点列表</param>
    Task NotifyGenICamNodesChangedAsync(Guid cameraId, List<GenICamNodeChangeDto> changes);

    /// <summary>
    /// 通过 SignalR 向所有客户端推送 NodeMap 已整体重新枚举的通知
    /// </summary>
    /// <param name="cameraId">相机设备 ID</param>
    /// <param name="enumeratedAt">枚举完成时间（UTC）</param>
    Task NotifyGenICamNodeMapReloadedAsync(Guid cameraId, DateTime enumeratedAt);

    /// <summary>
    /// 查询指定相机是否当前正在预览（用于刷新/重连后恢复 UI 状态）
    /// </summary>
    bool IsPreviewActive(Guid cameraId);

    /// <summary>
    /// 客户端在宽限期内重新订阅相机预览：取消挂起的延迟停止，并将会话连接 ID 续约为新连接。
    /// 返回 true 表示成功取消挂起停止（说明该相机预览确实因断开而进入宽限期）。
    /// </summary>
    bool ReattachPreviewAsync(Guid cameraId, string? newConnectionId);
}
