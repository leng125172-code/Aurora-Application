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
}
