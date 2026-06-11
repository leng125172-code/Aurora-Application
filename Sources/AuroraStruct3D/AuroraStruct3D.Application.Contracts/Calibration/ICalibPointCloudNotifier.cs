using AuroraStruct3D.Calibration.Dtos;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// Step7 点云生成实时通知器接口。
/// 由 HttpApi.Host 通过 SignalR 实现，向前端推送生成状态与进度。
/// </summary>
public interface ICalibPointCloudNotifier
{
    /// <summary>
    /// 推送点云生成状态变更（含进度）。
    /// </summary>
    Task NotifyStatusAsync(PointCloudStatusDto status);
}
