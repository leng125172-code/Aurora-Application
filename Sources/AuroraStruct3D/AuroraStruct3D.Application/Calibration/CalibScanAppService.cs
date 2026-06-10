using AuroraStruct3D.Calibration.Dtos;
using AuroraStruct3D.Cameras;
using AuroraStruct3D.Cameras.Dtos;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// Step6 在线扫描应用服务实现。
/// 首版先提供会话生命周期与状态观测能力，为后续实时重建算法接入预留扩展位。
/// </summary>
[Authorize]
public class CalibScanAppService : AuroraStruct3DAppService, ICalibScanAppService
{
    private readonly IRepository<CalibProject, Guid> _projectRepository;
    private readonly CalibScanStateStore _stateStore;
    private readonly ICalibScanNotifier _scanNotifier;
    private readonly ICameraDeviceAppService _cameraService;
    private readonly ILogger<CalibScanAppService> _logger;

    /// <summary>构造注入</summary>
    public CalibScanAppService(
        IRepository<CalibProject, Guid> projectRepository,
        CalibScanStateStore stateStore,
        ICalibScanNotifier scanNotifier,
        ICameraDeviceAppService cameraService,
        ILogger<CalibScanAppService> logger
    )
    {
        _projectRepository = projectRepository;
        _stateStore = stateStore;
        _scanNotifier = scanNotifier;
        _cameraService = cameraService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<CalibScanStatusDto> StartAsync(StartCalibScanInput input)
    {
        CalibProject project = await _projectRepository.GetAsync(input.CalibProjectId);
        CalibScanMode mode = ResolveAndValidateScanMode(project, input.ScanMode);

        ValidateBindingsForScanMode(project, mode);

        CalibScanSessionState session = _stateStore.Start(project.Id, mode);

        await EnsurePreviewStartedAsync(project, mode);

        DateTime lastMetricAt = DateTime.UtcNow;
        _stateStore.StartMetricLoop(
            project.Id,
            async frameIndex =>
            {
                DateTime now = DateTime.UtcNow;
                DateTime previousMetricAt = lastMetricAt;
                lastMetricAt = now;
                return await BuildRealtimeMetricsAsync(
                    project,
                    mode,
                    previousMetricAt,
                    now,
                    frameIndex
                );
            },
            metrics => _scanNotifier.NotifyMetricsAsync(project.Id, metrics)
        );

        _logger.LogInformation(
            "Step6 扫描启动：ProjectId={ProjectId}, Mode={Mode}",
            project.Id,
            mode
        );

        CalibScanStatusDto status = ToStatusDto(session);
        await _scanNotifier.NotifyStateAsync(status);
        return status;
    }

    private async Task<CalibScanMetricsDto> BuildRealtimeMetricsAsync(
        CalibProject project,
        CalibScanMode mode,
        DateTime previousMetricAt,
        DateTime now,
        long frameIndex
    )
    {
        double elapsedSeconds = Math.Max((now - previousMetricAt).TotalSeconds, 1e-3);
        double fps = Math.Round(1d / elapsedSeconds, 2);

        if (mode is CalibScanMode.TwoCamera0Light or CalibScanMode.TwoCamera1Light)
        {
            if (!project.MainCameraDeviceId.HasValue || !project.SecondaryCameraDeviceId.HasValue)
            {
                return new CalibScanMetricsDto
                {
                    Fps = fps,
                    DepthValidRate = 0,
                    Confidence = 0,
                    FrameIndex = frameIndex,
                    Timestamp = now,
                };
            }

            CameraSnapshotDto leftSnapshot = await _cameraService.TakeSnapshotAsync(
                project.MainCameraDeviceId.Value
            );
            CameraSnapshotDto rightSnapshot = await _cameraService.TakeSnapshotAsync(
                project.SecondaryCameraDeviceId.Value
            );

            byte[] leftBytes = ExtractJpegBytes(leftSnapshot.DataUri);
            byte[] rightBytes = ExtractJpegBytes(rightSnapshot.DataUri);

            (double depthValidRate, double confidence) = ComputeStereoMetrics(leftBytes, rightBytes);

            return new CalibScanMetricsDto
            {
                Fps = fps,
                DepthValidRate = depthValidRate,
                Confidence = confidence,
                FrameIndex = frameIndex,
                Timestamp = now,
            };
        }

        if (!project.MainCameraDeviceId.HasValue)
        {
            return new CalibScanMetricsDto
            {
                Fps = fps,
                DepthValidRate = 0,
                Confidence = 0,
                FrameIndex = frameIndex,
                Timestamp = now,
            };
        }

        CameraSnapshotDto snapshot = await _cameraService.TakeSnapshotAsync(
            project.MainCameraDeviceId.Value
        );
        byte[] bytes = ExtractJpegBytes(snapshot.DataUri);

        using Mat gray = Cv2.ImDecode(bytes, ImreadModes.Grayscale);
        double confidenceMono = NormalizeSharpness(ComputeLaplacianVariance(gray));

        return new CalibScanMetricsDto
        {
            Fps = fps,
            DepthValidRate = 0,
            Confidence = confidenceMono,
            FrameIndex = frameIndex,
            Timestamp = now,
        };
    }

    private static (double depthValidRate, double confidence) ComputeStereoMetrics(
        byte[] leftBytes,
        byte[] rightBytes
    )
    {
        using Mat leftGray = Cv2.ImDecode(leftBytes, ImreadModes.Grayscale);
        using Mat rightGray = Cv2.ImDecode(rightBytes, ImreadModes.Grayscale);

        if (leftGray.Empty() || rightGray.Empty())
        {
            return (0, 0);
        }

        using Mat left = ResizeForStereo(leftGray);
        using Mat right = ResizeForStereo(rightGray);

        using Mat disparity16 = new();
        using StereoBM stereo = StereoBM.Create(numDisparities: 96, blockSize: 15);
        stereo.Compute(left, right, disparity16);

        using Mat validMask = new();
        Cv2.Compare(disparity16, Scalar.All(0), validMask, CmpTypes.GT);

        int total = validMask.Rows * validMask.Cols;
        int valid = Cv2.CountNonZero(validMask);
        double depthValidRate = total > 0 ? (double)valid / total : 0;

        double leftSharpness = NormalizeSharpness(ComputeLaplacianVariance(left));
        double rightSharpness = NormalizeSharpness(ComputeLaplacianVariance(right));
        double sharpness = (leftSharpness + rightSharpness) / 2d;

        double confidence = Math.Clamp(depthValidRate * 0.7d + sharpness * 0.3d, 0d, 1d);
        return (Math.Clamp(depthValidRate, 0d, 1d), confidence);
    }

    private static Mat ResizeForStereo(Mat gray)
    {
        int targetWidth = Math.Min(640, gray.Width);
        double scale = targetWidth / (double)gray.Width;
        int targetHeight = Math.Max(64, (int)Math.Round(gray.Height * scale));

        Mat resized = new();
        Cv2.Resize(gray, resized, new Size(targetWidth, targetHeight), 0, 0, InterpolationFlags.Area);
        return resized;
    }

    private static double ComputeLaplacianVariance(Mat gray)
    {
        if (gray.Empty())
        {
            return 0;
        }

        using Mat lap = new();
        Cv2.Laplacian(gray, lap, MatType.CV_64F);
        Cv2.MeanStdDev(lap, out _, out Scalar stddev);
        return stddev.Val0 * stddev.Val0;
    }

    private static double NormalizeSharpness(double variance)
    {
        const double baseline = 50d;
        const double scale = 200d;
        return Math.Clamp((variance - baseline) / scale, 0d, 1d);
    }

    private static byte[] ExtractJpegBytes(string dataUri)
    {
        const string marker = ";base64,";
        int idx = dataUri.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        string base64 = idx >= 0 ? dataUri[(idx + marker.Length)..] : dataUri;
        return Convert.FromBase64String(base64);
    }

    /// <inheritdoc/>
    public async Task<CalibScanStatusDto> StopAsync(StopCalibScanInput input)
    {
        CalibProject project = await _projectRepository.GetAsync(input.CalibProjectId);
        CalibScanSessionState? stopped = _stateStore.Stop(project.Id);

        if (stopped is null)
        {
            CalibScanStatusDto idleStatus = new()
            {
                CalibProjectId = project.Id,
                ScanMode = ResolveAndValidateScanMode(project, null),
                State = CalibScanRunState.Idle,
                IsRunning = false,
                LastUpdatedAt = DateTime.UtcNow,
            };

            await _scanNotifier.NotifyStateAsync(idleStatus);
            return idleStatus;
        }

        await EnsurePreviewStoppedAsync(project, stopped.ScanMode);

        _logger.LogInformation("Step6 扫描停止：ProjectId={ProjectId}", project.Id);
        CalibScanStatusDto status = ToStatusDto(stopped);
        await _scanNotifier.NotifyStateAsync(status);
        return status;
    }

    /// <inheritdoc/>
    public async Task<CalibScanStatusDto> GetStatusAsync(Guid calibProjectId)
    {
        CalibProject project = await _projectRepository.GetAsync(calibProjectId);
        CalibScanSessionState? session = _stateStore.TryGet(calibProjectId);

        if (session is null)
        {
            return new CalibScanStatusDto
            {
                CalibProjectId = project.Id,
                ScanMode = ResolveAndValidateScanMode(project, null),
                State = CalibScanRunState.Idle,
                IsRunning = false,
                LastUpdatedAt = DateTime.UtcNow,
            };
        }

        return ToStatusDto(session);
    }

    private static CalibScanStatusDto ToStatusDto(CalibScanSessionState session)
    {
        return new CalibScanStatusDto
        {
            CalibProjectId = session.CalibProjectId,
            ScanMode = session.ScanMode,
            State = session.State,
            IsRunning = session.IsRunning,
            StartedAt = session.StartedAt,
            LastUpdatedAt = session.LastUpdatedAt,
            ErrorMessage = session.ErrorMessage,
            LatestMetrics = session.LatestMetrics,
        };
    }

    private static CalibScanMode ResolveAndValidateScanMode(
        CalibProject project,
        CalibScanMode? requestedMode
    )
    {
        CalibScanMode expectedMode = project.DeviceType switch
        {
            CalibDeviceType.TwoCamera0Light => CalibScanMode.TwoCamera0Light,
            CalibDeviceType.OneCamera1Light => CalibScanMode.OneCamera1Light,
            CalibDeviceType.TwoCamera1Light => CalibScanMode.TwoCamera1Light,
            _ => throw new UserFriendlyException("当前项目设备类型不支持在线扫描"),
        };

        if (requestedMode.HasValue && requestedMode.Value != expectedMode)
        {
            throw new UserFriendlyException("扫描模式与当前项目设备类型不匹配");
        }

        return requestedMode ?? expectedMode;
    }

    private static void ValidateBindingsForScanMode(CalibProject project, CalibScanMode scanMode)
    {
        if (!project.MainCameraDeviceId.HasValue)
        {
            throw new UserFriendlyException("请先绑定主相机后再启动在线扫描");
        }

        if (scanMode is CalibScanMode.TwoCamera0Light or CalibScanMode.TwoCamera1Light)
        {
            if (!project.SecondaryCameraDeviceId.HasValue)
            {
                throw new UserFriendlyException("双目扫描模式下必须绑定从相机");
            }
        }

        if (scanMode is CalibScanMode.OneCamera1Light or CalibScanMode.TwoCamera1Light)
        {
            if (!project.BoundProjectorDeviceId.HasValue)
            {
                throw new UserFriendlyException("含结构光扫描模式下必须绑定投影机");
            }
        }
    }

    private async Task EnsurePreviewStartedAsync(CalibProject project, CalibScanMode scanMode)
    {
        if (!project.MainCameraDeviceId.HasValue)
        {
            return;
        }

        await _cameraService.StartPreviewAsync(
            project.MainCameraDeviceId.Value,
            new StartCameraPreviewDto { EnableRtp = false, ConnectionId = null }
        );

        if (
            scanMode is CalibScanMode.TwoCamera0Light or CalibScanMode.TwoCamera1Light
            && project.SecondaryCameraDeviceId.HasValue
        )
        {
            await _cameraService.StartPreviewAsync(
                project.SecondaryCameraDeviceId.Value,
                new StartCameraPreviewDto { EnableRtp = false, ConnectionId = null }
            );
        }
    }

    private async Task EnsurePreviewStoppedAsync(CalibProject project, CalibScanMode scanMode)
    {
        if (project.MainCameraDeviceId.HasValue)
        {
            await _cameraService.StopPreviewAsync(project.MainCameraDeviceId.Value);
        }

        if (
            scanMode is CalibScanMode.TwoCamera0Light or CalibScanMode.TwoCamera1Light
            && project.SecondaryCameraDeviceId.HasValue
        )
        {
            await _cameraService.StopPreviewAsync(project.SecondaryCameraDeviceId.Value);
        }
    }
}
