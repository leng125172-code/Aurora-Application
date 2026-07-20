using AuroraStruct3D.Calibration.Dtos;
using Volo.Abp.DependencyInjection;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// Step6 扫描通知器空实现。
/// 当宿主层未注册 SignalR 推送实现时，作为默认占位，避免服务构造失败。
/// </summary>
[Dependency(TryRegister = true)]
[ExposeServices(typeof(ICalibScanNotifier))]
public class NullCalibScanNotifier : ICalibScanNotifier, ISingletonDependency
{
    /// <inheritdoc/>
    public Task NotifyStateAsync(CalibScanStatusDto status)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task NotifyMetricsAsync(Guid calibProjectId, CalibScanMetricsDto metrics)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task NotifyFrameAsync(
        Guid calibProjectId,
        int cameraRole,
        byte[] jpegBytes,
        long roundIndex,
        int frameIndexInRound
    )
    {
        return Task.CompletedTask;
    }
}
