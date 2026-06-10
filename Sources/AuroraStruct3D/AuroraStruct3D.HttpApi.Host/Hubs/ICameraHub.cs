using AuroraStruct3D.Cameras.Dtos;
using AuroraStruct3D.Calibration.Dtos;

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

    /// <summary>
    /// 推送一批 GenICam 节点的增量变更（Set 节点或 Selector 切换导致的依赖节点变化）。
    /// 前端按 NodeName 在本地 NodeMap 中 patch Value/Access/IsLocked，无需重新枚举整张表。
    /// </summary>
    /// <param name="cameraId">相机设备 ID</param>
    /// <param name="changes">变更节点列表</param>
    Task OnGenICamNodesChangedAsync(string cameraId, List<GenICamNodeChangeDto> changes);

    /// <summary>
    /// 推送 GenICam NodeMap 已整体重新枚举的通知（如调用 RefreshNodeMap 后）。
    /// 前端收到后应重新拉取完整 NodeMap。
    /// </summary>
    /// <param name="cameraId">相机设备 ID</param>
    /// <param name="enumeratedAt">枚举完成时间（UTC）</param>
    Task OnGenICamNodeMapReloadedAsync(string cameraId, DateTime enumeratedAt);

    /// <summary>
    /// 推送 Step6 扫描状态变化。
    /// </summary>
    Task ReceiveCalibScanStateAsync(CalibScanStatusDto status);

    /// <summary>
    /// 推送 Step6 扫描实时指标。
    /// </summary>
    Task ReceiveCalibScanMetricsAsync(string calibProjectId, CalibScanMetricsDto metrics);
}
