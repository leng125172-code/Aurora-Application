using AuroraStruct3D.Cameras.Dtos;

namespace AuroraStruct3D.Hubs;

/// <summary>
/// 相机实时推流 SignalR Hub 的客户端推送接口
/// </summary>
public interface ICameraHub
{
    /// <summary>
    /// 推送一帧 JPEG 图像数据（用于实时预览）
    /// </summary>
    /// <param name="cameraId">相机设备 ID</param>
    /// <param name="frame">JPEG 编码的帧字节数组</param>
    Task ReceiveCameraFrameAsync(string cameraId, byte[] frame);

    /// <summary>
    /// 推送相机状态变更通知（统一使用 CameraStateDto 携带完整状态快照）
    /// </summary>
    /// <param name="state">相机状态 DTO（含温度、采集状态、NodeMap 加载标记等）</param>
    Task ReceiveCameraStateAsync(CameraStateDto state);

    /// <summary>
    /// 推送相机实时运行指标（温度/帧率/AE状态等）
    /// </summary>
    /// <param name="cameraId">相机设备 ID</param>
    /// <param name="metrics">实时指标 DTO</param>
    Task ReceiveLiveMetricsAsync(string cameraId, CameraLiveMetricsDto metrics);
}
