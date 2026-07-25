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

    /// <summary>
    /// 推送增量点云数据（实时迭代）。
    /// </summary>
    /// <param name="calibProjectId">标定项目 ID</param>
    /// <param name="pointCloudBytes">点云数据（压缩后的字节数组）</param>
    /// <param name="pointCount">新增点数量</param>
    /// <param name="totalPointCount">累积总点数量</param>
    Task NotifyIncrementalPointCloudAsync(
        Guid calibProjectId,
        byte[] pointCloudBytes,
        int pointCount,
        int totalPointCount
    );
}
