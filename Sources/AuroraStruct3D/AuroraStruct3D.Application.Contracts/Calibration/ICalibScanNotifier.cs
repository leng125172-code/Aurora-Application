using AuroraStruct3D.Calibration.Dtos;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// Step6 在线扫描实时通知器接口。
/// 由 HttpApi.Host 通过 SignalR 实现，向前端推送扫描状态与实时指标。
/// </summary>
public interface ICalibScanNotifier
{
    /// <summary>
    /// 推送扫描状态变更。
    /// </summary>
    Task NotifyStateAsync(CalibScanStatusDto status);

    /// <summary>
    /// 推送扫描实时指标。
    /// </summary>
    Task NotifyMetricsAsync(Guid calibProjectId, CalibScanMetricsDto metrics);

    /// <summary>
    /// 推送扫描单帧主/从相机原图（JPEG 二进制）。
    /// </summary>
    /// <param name="calibProjectId">标定项目 ID</param>
    /// <param name="cameraRole">相机角色（0=主相机, 1=从相机）</param>
    /// <param name="jpegBytes">JPEG 原图二进制数据</param>
    /// <param name="roundIndex">当前轮次序号</param>
    /// <param name="frameIndexInRound">当前轮内帧序号</param>
    Task NotifyFrameAsync(
        Guid calibProjectId,
        int cameraRole,
        byte[] jpegBytes,
        long roundIndex,
        int frameIndexInRound
    );
}
