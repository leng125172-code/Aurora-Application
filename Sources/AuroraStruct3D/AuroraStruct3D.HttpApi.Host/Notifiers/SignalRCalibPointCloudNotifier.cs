using AuroraStruct3D.Calibration;
using AuroraStruct3D.Calibration.Dtos;
using AuroraStruct3D.Hubs;
using Microsoft.AspNetCore.SignalR;
using Volo.Abp.DependencyInjection;

namespace AuroraStruct3D.Notifiers;

/// <summary>
/// 基于 CameraHub 的 Step7 点云生成 SignalR 推送实现。
/// </summary>
[ExposeServices(typeof(ICalibPointCloudNotifier))]
public class SignalRCalibPointCloudNotifier : ICalibPointCloudNotifier, ISingletonDependency
{
    private readonly IHubContext<CameraHub, ICameraHub> _hubContext;

    public SignalRCalibPointCloudNotifier(IHubContext<CameraHub, ICameraHub> hubContext)
    {
        _hubContext = hubContext;
    }

    private static string BuildProjectGroup(Guid calibProjectId)
    {
        return $"calib-point-cloud:{calibProjectId:N}";
    }

    /// <inheritdoc/>
    public Task NotifyStatusAsync(PointCloudStatusDto status)
    {
        return _hubContext
            .Clients.Group(BuildProjectGroup(status.CalibProjectId))
            .ReceivePointCloudStatusAsync(status);
    }
}
