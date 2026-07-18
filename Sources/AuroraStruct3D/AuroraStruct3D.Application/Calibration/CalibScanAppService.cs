using AuroraStruct3D.Calibration.Dtos;
using AuroraStruct3D.Cameras;
using AuroraStruct3D.Tucam;
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
    private readonly IRepository<CameraDevice, Guid> _cameraDeviceRepository;
    private readonly ITucamCameraService _tucamService;
    private readonly ILogger<CalibScanAppService> _logger;

    /// <summary>构造注入</summary>
    public CalibScanAppService(
        IRepository<CalibProject, Guid> projectRepository,
        CalibScanStateStore stateStore,
        ICalibScanNotifier scanNotifier,
        IRepository<CameraDevice, Guid> cameraDeviceRepository,
        ITucamCameraService tucamService,
        ILogger<CalibScanAppService> logger
    )
    {
        _projectRepository = projectRepository;
        _stateStore = stateStore;
        _scanNotifier = scanNotifier;
        _cameraDeviceRepository = cameraDeviceRepository;
        _tucamService = tucamService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<CalibScanStatusDto> StartAsync(StartCalibScanInput input)
    {
        CalibProject project = await _projectRepository.GetAsync(input.CalibProjectId);
        CalibScanMode mode = ResolveScanMode(project);

        ValidateBindingsForScanMode(project, mode);

        CalibScanSessionState session = _stateStore.Start(project.Id, mode);

        DateTime lastMetricAt = DateTime.UtcNow;
        _stateStore.StartMetricLoop(
            project.Id,
            async frameIndex =>
            {
                DateTime now = DateTime.UtcNow;
                DateTime previousMetricAt = lastMetricAt;
                lastMetricAt = now;
                bool imageEnhance = _stateStore.GetImageEnhanceEnabled(project.Id);
                return await BuildRealtimeMetricsAsync(
                    project,
                    mode,
                    previousMetricAt,
                    now,
                    frameIndex,
                    imageEnhance
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
        long frameIndex,
        bool imageEnhance
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
                    DepthMapDataUri = null,
                    FrameIndex = frameIndex,
                    Timestamp = now,
                };
            }

            // 按照测试结论（TucamMultiCameraProbe）：顺序单活采集——主相机先拍，从相机再拍
            // _capStartActiveLock 保证同一时刻只有一台相机处于 Cap_Start 状态
            byte[]? leftBytes = await GrabMetricFrameAsync(project.MainCameraDeviceId.Value);
            byte[]? rightBytes = await GrabMetricFrameAsync(project.SecondaryCameraDeviceId.Value);

            if (leftBytes == null || rightBytes == null)
            {
                return new CalibScanMetricsDto
                {
                    Fps = fps,
                    DepthValidRate = 0,
                    Confidence = 0,
                    DepthMapDataUri = null,
                    FrameIndex = frameIndex,
                    Timestamp = now,
                };
            }

            if (imageEnhance)
            {
                leftBytes = ApplyClahe(leftBytes);
                rightBytes = ApplyClahe(rightBytes);
            }

            (double depthValidRate, double confidence, string? depthMapDataUri) =
                ComputeStereoMetrics(leftBytes, rightBytes);

            return new CalibScanMetricsDto
            {
                Fps = fps,
                DepthValidRate = depthValidRate,
                Confidence = confidence,
                DepthMapDataUri = depthMapDataUri,
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
                DepthMapDataUri = null,
                FrameIndex = frameIndex,
                Timestamp = now,
            };
        }

        byte[]? bytes = await GrabMetricFrameAsync(project.MainCameraDeviceId.Value);

        if (bytes == null)
        {
            return new CalibScanMetricsDto
            {
                Fps = fps,
                DepthValidRate = 0,
                Confidence = 0,
                DepthMapDataUri = null,
                FrameIndex = frameIndex,
                Timestamp = now,
            };
        }

        if (imageEnhance)
        {
            bytes = ApplyClahe(bytes);
        }

        using Mat gray = Cv2.ImDecode(bytes, ImreadModes.Grayscale);
        double confidenceMono = NormalizeSharpness(ComputeLaplacianVariance(gray));

        return new CalibScanMetricsDto
        {
            Fps = fps,
            DepthValidRate = 0,
            Confidence = confidenceMono,
            DepthMapDataUri = null,
            FrameIndex = frameIndex,
            Timestamp = now,
        };
    }

    private static (
        double depthValidRate,
        double confidence,
        string? depthMapDataUri
    ) ComputeStereoMetrics(byte[] leftBytes, byte[] rightBytes)
    {
        using Mat leftGray = Cv2.ImDecode(leftBytes, ImreadModes.Grayscale);
        using Mat rightGray = Cv2.ImDecode(rightBytes, ImreadModes.Grayscale);

        if (leftGray.Empty() || rightGray.Empty())
        {
            return (0, 0, null);
        }

        using Mat left = ResizeForStereo(leftGray);
        using Mat right = ResizeForStereo(rightGray);

        using Mat disparity16 = new();
        const int StereoNumDisparities = 96;
        const int StereoBlockSize = 15;
        using StereoBM stereo = StereoBM.Create(numDisparities: StereoNumDisparities, blockSize: StereoBlockSize);
        stereo.Compute(left, right, disparity16);

        using Mat validMask = new();
        Cv2.Compare(disparity16, Scalar.All(0), validMask, CmpTypes.GT);

        using Mat disparity8 = new();
        Cv2.ConvertScaleAbs(disparity16, disparity8, 255d / (StereoNumDisparities * 16d));

        using Mat depthColor = new();
        Cv2.ApplyColorMap(disparity8, depthColor, ColormapTypes.Jet);

        using Mat depthBgr = new();
        depthColor.CopyTo(depthBgr);
        using Mat invalidMask = new();
        Cv2.BitwiseNot(validMask, invalidMask);
        depthBgr.SetTo(Scalar.All(0), invalidMask);

        int total = validMask.Rows * validMask.Cols;
        int valid = Cv2.CountNonZero(validMask);
        double depthValidRate = total > 0 ? (double)valid / total : 0;

        double leftSharpness = NormalizeSharpness(ComputeLaplacianVariance(left));
        double rightSharpness = NormalizeSharpness(ComputeLaplacianVariance(right));
        double sharpness = (leftSharpness + rightSharpness) / 2d;

        double confidence = Math.Clamp(depthValidRate * 0.7d + sharpness * 0.3d, 0d, 1d);
        string? depthMapDataUri = BuildJpegDataUri(depthBgr);
        return (Math.Clamp(depthValidRate, 0d, 1d), confidence, depthMapDataUri);
    }

    private static string? BuildJpegDataUri(Mat image)
    {
        if (image.Empty())
        {
            return null;
        }

        if (!Cv2.ImEncode(".jpg", image, out byte[] encoded) || encoded.Length == 0)
        {
            return null;
        }

        return $"data:image/jpeg;base64,{Convert.ToBase64String(encoded)}";
    }

    private static Mat ResizeForStereo(Mat gray)
    {
        const int TargetWidth = 640;
        const int MinHeight = 64;
        int targetWidth = Math.Min(TargetWidth, gray.Width);
        double scale = targetWidth / (double)gray.Width;
        int targetHeight = Math.Max(MinHeight, (int)Math.Round(gray.Height * scale));

        Mat resized = new();
        Cv2.Resize(
            gray,
            resized,
            new Size(targetWidth, targetHeight),
            0,
            0,
            InterpolationFlags.Area
        );
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

    /// <summary>
    /// 使用 OpenCV CLAHE 算法对 JPEG 字节帧进行对比度限制自适应直方图均衡，
    /// 增强图像细节，改善低光或曝光不足场景下的视觉质量。
    /// </summary>
    private static byte[] ApplyClahe(byte[] jpegBytes)
    {
        using Mat src = Cv2.ImDecode(jpegBytes, ImreadModes.Grayscale);
        if (src.Empty())
        {
            return jpegBytes;
        }

        using CLAHE clahe = Cv2.CreateCLAHE(clipLimit: 2.0, tileGridSize: new Size(8, 8));
        using Mat enhanced = new();
        clahe.Apply(src, enhanced);

        if (!Cv2.ImEncode(".jpg", enhanced, out byte[] result) || result.Length == 0)
        {
            return jpegBytes;
        }

        return result;
    }

    /// <summary>
    /// Step6 指标帧抓取：直接调用 TucamCameraService 进行单帧采集。
    /// 不经过 ICameraDeviceAppService.TakeSnapshotAsync，避免手动模式检查限制。
    /// 遵循测试结论（TucamMultiCameraProbe）：Cap_Start → 触发 → WaitForFrame → Cap_Stop，
    /// _capStartActiveLock 自动保证同一时刻仅一台相机处于活跃采集状态。
    /// </summary>
    private async Task<byte[]?> GrabMetricFrameAsync(Guid cameraDeviceId)
    {
        CameraDevice camera;
        try
        {
            camera = await _cameraDeviceRepository.GetAsync(cameraDeviceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Step6 指标帧抓取：相机设备 {Id} 不存在", cameraDeviceId);
            return null;
        }

        int idx = camera.DeviceIndex;
        if (!_tucamService.IsCameraOpen(idx))
        {
            _logger.LogDebug("Step6 指标帧抓取：相机 {Index} 未打开，跳过", idx);
            return null;
        }

        try
        {
            await _tucamService.StartCaptureAsync(idx);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Step6 指标帧抓取：相机 {Index} 启动采集失败", idx);
            return null;
        }

        try
        {
            // 软件触发模式（TriggerMode=2）需要手动发送触发脉冲，否则 WaitForFrame 不返回
            long triggerMode = 0;
            try
            {
                triggerMode = await _tucamService.GetGenICamIntAsync(idx, "TriggerMode");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Step6 指标帧抓取：相机 {Index} 读取触发模式失败，按自由运行处理", idx);
            }

            if (triggerMode == 2)
            {
                await _tucamService.DoSoftwareTriggerAsync(idx);
            }

            // 动态计算超时：曝光时间（微秒）× 2 + 1s 裕量，最少 8s
            int timeoutMs = 8000;
            try
            {
                long exposureUs = await _tucamService.GetGenICamIntAsync(idx, "ExposureTime");
                timeoutMs = Math.Max((int)(exposureUs / 1000L) * 2 + 1000, 8000);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Step6 指标帧抓取：相机 {Index} 读取曝光时间失败，使用默认超时 {TimeoutMs}ms", idx, timeoutMs);
            }

            (byte[] jpegBytes, _) = await _tucamService.GrabFrameRawAsync(idx, timeoutMs);
            return jpegBytes;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Step6 指标帧抓取：相机 {Index} 抓帧失败", idx);
            return null;
        }
        finally
        {
            try
            {
                await _tucamService.StopCaptureAsync(idx);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Step6 指标帧抓取：相机 {Index} 停止采集失败", idx);
            }
        }
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
                State = CalibScanRunState.Idle,
                IsRunning = false,
                LastUpdatedAt = DateTime.UtcNow,
            };

            await _scanNotifier.NotifyStateAsync(idleStatus);
            return idleStatus;
        }

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
            State = session.State,
            IsRunning = session.IsRunning,
            StartedAt = session.StartedAt,
            LastUpdatedAt = session.LastUpdatedAt,
            ErrorMessage = session.ErrorMessage,
            LatestMetrics = session.LatestMetrics,
        };
    }

    /// <inheritdoc/>
    public Task SetImageEnhanceAsync(SetCalibScanImageEnhanceInput input)
    {
        _stateStore.SetImageEnhance(input.CalibProjectId, input.Enabled);
        _logger.LogInformation(
            "Step6 图像增强已{Status}：ProjectId={ProjectId}",
            input.Enabled ? "启用" : "禁用",
            input.CalibProjectId
        );
        return Task.CompletedTask;
    }

    private static CalibScanMode ResolveScanMode(CalibProject project)
    {
        return project.DeviceType switch
        {
            CalibDeviceType.TwoCamera0Light => CalibScanMode.TwoCamera0Light,
            CalibDeviceType.OneCamera1Light => CalibScanMode.OneCamera1Light,
            CalibDeviceType.TwoCamera1Light => CalibScanMode.TwoCamera1Light,
            _ => throw new UserFriendlyException("当前项目设备类型不支持在线扫描"),
        };
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
                throw new UserFriendlyException("含结构光扫描模式下必须绑定投影仪");
            }
        }
    }
}
