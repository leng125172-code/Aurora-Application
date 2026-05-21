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
    Task StartPreviewAsync(Guid cameraId, string? connectionId, bool enableRtp);

    /// <summary>
    /// 停止指定相机的实时推流
    /// </summary>
    Task StopPreviewAsync(Guid cameraId);

    /// <summary>
    /// 获取指定相机的 RTP/MJPEG 推流端点信息；相机未在推流时返回 null
    /// </summary>
    CameraRtpEndpointDto? GetRtpEndpoint(Guid cameraId);
}
