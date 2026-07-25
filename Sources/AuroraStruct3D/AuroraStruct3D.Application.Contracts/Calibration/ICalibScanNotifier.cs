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
    /// 发布扫描单帧主/从相机原图。
    /// Host 将图像写入最新帧缓冲，并通过 HTTP Multipart 提供给前端。
    /// </summary>
    /// <param name="calibProjectId">标定项目 ID</param>
    /// <param name="cameraRole">相机角色（0=主相机, 1=从相机）</param>
    /// <param name="jpegBytes">编码后的完整图像字节（当前为 BMP）</param>
    /// <param name="roundIndex">当前轮次序号</param>
    /// <param name="frameIndexInRound">当前轮内帧序号</param>
    /// <param name="totalFrameCountPerRound">每轮总帧数</param>
    /// <param name="accumulatedFrameCount">累计帧数</param>
    /// <param name="isCrosshairDetected">十字图检测结果</param>
    Task NotifyFrameAsync(
        Guid calibProjectId,
        int cameraRole,
        byte[] jpegBytes,
        long roundIndex,
        int frameIndexInRound,
        int totalFrameCountPerRound,
        long accumulatedFrameCount,
        bool isCrosshairDetected
    );
}
