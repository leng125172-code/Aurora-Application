using AuroraStruct3D.Calibration;
using AuroraStruct3D.Calibration.Dtos;
using AuroraStruct3D.Hubs;
using AuroraStruct3D.Streaming;
using Microsoft.AspNetCore.SignalR;
using Volo.Abp.DependencyInjection;

namespace AuroraStruct3D.Notifiers;

/// <summary>
/// 基于 CameraHub 的 Step6 扫描 SignalR 推送实现。
/// </summary>
[ExposeServices(typeof(ICalibScanNotifier))]
public class SignalRCalibScanNotifier : ICalibScanNotifier, ISingletonDependency
{
    private readonly IHubContext<CameraHub, ICameraHub> _hubContext;
    private readonly CalibScanFrameBufferService _frameBuffer;

    private static string BuildProjectGroup(Guid calibProjectId)
    {
        return $"calib-scan:{calibProjectId:N}";
    }

    public SignalRCalibScanNotifier(
        IHubContext<CameraHub, ICameraHub> hubContext,
        CalibScanFrameBufferService frameBuffer
    )
    {
        _hubContext = hubContext;
        _frameBuffer = frameBuffer;
    }

    /// <inheritdoc/>
    public Task NotifyStateAsync(CalibScanStatusDto status)
    {
        return _hubContext
            .Clients.Group(BuildProjectGroup(status.CalibProjectId))
            .ReceiveCalibScanStateAsync(status);
    }

    /// <inheritdoc/>
    public Task NotifyMetricsAsync(Guid calibProjectId, CalibScanMetricsDto metrics)
    {
        return _hubContext
            .Clients.Group(BuildProjectGroup(calibProjectId))
            .ReceiveCalibScanMetricsAsync(calibProjectId.ToString(), metrics);
    }

    /// <inheritdoc/>
    public Task NotifyFrameAsync(
        Guid calibProjectId,
        int cameraRole,
        byte[] jpegBytes,
        long roundIndex,
        int frameIndexInRound,
        int totalFrameCountPerRound,
        long accumulatedFrameCount,
        bool isCrosshairDetected
    )
    {
        _frameBuffer.PublishFrame(calibProjectId, cameraRole, jpegBytes);
        return Task.CompletedTask;
    }
}
