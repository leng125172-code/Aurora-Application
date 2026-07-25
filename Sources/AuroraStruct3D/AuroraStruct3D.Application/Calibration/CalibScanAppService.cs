using AuroraStruct3D.Calibration.Dtos;
using AuroraStruct3D.Cameras;
using AuroraStruct3D.Projectors;
using AuroraStruct3D.Tucam;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// Step6 在线扫描应用服务实现。
/// 含投影仪设备类型（OneCamera1Light / TwoCamera1Light）执行完整结构光扫描流程：
/// 投影仪投射条纹图（T/N 指令）+ 双目相机软件触发顺序抓拍 + 实时原图推送。
/// 无投影仪设备类型（TwoCamera0Light）执行简化双目抓拍推送。
/// </summary>
[Authorize]
public class CalibScanAppService : AuroraStruct3DAppService, ICalibScanAppService
{
    private readonly IRepository<CalibProject, Guid> _projectRepository;
    private readonly CalibScanStateStore _stateStore;
    private readonly ICalibScanNotifier _scanNotifier;
    private readonly IRepository<CameraDevice, Guid> _cameraDeviceRepository;
    private readonly ITucamCameraService _tucamService;
    private readonly IRepository<CalibProjectorParam, Guid> _projectorParamRepository;
    private readonly IProjectorConnectionPool _projectorConnectionPool;
    private readonly ILogger<CalibScanAppService> _logger;
    private readonly IBlobContainer<CalibPhotoBlobContainer> _blobContainer;
    private readonly ICalibPointCloudAppService _pointCloudAppService;

    /// <summary>构造注入</summary>
    public CalibScanAppService(
        IRepository<CalibProject, Guid> projectRepository,
        CalibScanStateStore stateStore,
        ICalibScanNotifier scanNotifier,
        IRepository<CameraDevice, Guid> cameraDeviceRepository,
        ITucamCameraService tucamService,
        IRepository<CalibProjectorParam, Guid> projectorParamRepository,
        IProjectorConnectionPool projectorConnectionPool,
        ILogger<CalibScanAppService> logger,
        IBlobContainer<CalibPhotoBlobContainer> blobContainer,
        ICalibPointCloudAppService pointCloudAppService
    )
    {
        _projectRepository = projectRepository;
        _stateStore = stateStore;
        _scanNotifier = scanNotifier;
        _cameraDeviceRepository = cameraDeviceRepository;
        _tucamService = tucamService;
        _projectorParamRepository = projectorParamRepository;
        _projectorConnectionPool = projectorConnectionPool;
        _logger = logger;
        _blobContainer = blobContainer;
        _pointCloudAppService = pointCloudAppService;
    }

    /// <inheritdoc/>
    public async Task<CalibScanStatusDto> StartAsync(StartCalibScanInput input)
    {
        CalibProject project = await _projectRepository.GetAsync(input.CalibProjectId);
        CalibScanMode mode = ResolveScanMode(project);

        ValidateBindingsForScanMode(project, mode);

        // 读取 Step3 投影仪参数获取 PatternCount（每个方向的相移步数）。
        // 因 Step3 固定按 1-2-1-2 横竖交替下载，实际每轮总帧数 = 2 × PatternCount。
        int patternCount = 0;
        int totalFrameCount = 0;
        IDlpProjectorService? projectorService = null;
        if (mode is CalibScanMode.OneCamera1Light or CalibScanMode.TwoCamera1Light)
        {
            CalibProjectorParam? param = await GetProjectorParamAsync(project);
            patternCount = param.PatternCount;
            if (patternCount <= 0)
            {
                throw new UserFriendlyException(
                    "Step3 投影仪参数中图案数量（PatternCount）无效，请先完成配置"
                );
            }

            totalFrameCount = patternCount * 2;

            projectorService = _projectorConnectionPool.TryGet(
                project.BoundProjectorDeviceId!.Value
            );
            if (projectorService == null)
            {
                throw new UserFriendlyException("投影仪尚未连接，请先在设备管理页连接投影仪");
            }

            // 初始化投影仪：S8 2（开机图为十字）→ LN（开灯）→ S2（切到十字图）
            await SafeProjectorInvokeAsync(
                projectorService,
                svc => svc.SetBootImageAsync(ProjectorBootImage.Cross),
                "SetBootImageAsync(Cross)"
            );
            await SafeProjectorInvokeAsync(projectorService, svc => svc.LedOnAsync(), "LedOnAsync");
            await SafeProjectorInvokeAsync(
                projectorService,
                svc => svc.SetDisplayModeAsync(ProjectorDisplayMode.Cross),
                "SetDisplayModeAsync(Cross)"
            );

            // 设置主从相机为软件触发模式（TriggerSource=Software=1, TriggerMode=On=2）
            await EnsureSoftwareTriggerModeAsync(project.MainCameraDeviceId!.Value);
            if (mode == CalibScanMode.TwoCamera1Light && project.SecondaryCameraDeviceId.HasValue)
            {
                await EnsureSoftwareTriggerModeAsync(project.SecondaryCameraDeviceId.Value);
            }

            // 投影仪切换到单帧触发模式（B 2）
            await SafeProjectorInvokeAsync(
                projectorService,
                svc => svc.SetTriggerModeAsync(ProjectorTriggerMode.SingleFrame),
                "SetTriggerModeAsync(SingleFrame)"
            );
        }

        CalibScanSessionState session = _stateStore.Start(project.Id, mode, totalFrameCount);

        // 启动扫描循环
        _stateStore.StartScanLoop(
            project.Id,
            ct => ScanLoopAsync(project, mode, totalFrameCount, patternCount, projectorService, ct)
        );

        _logger.LogInformation(
            "Step6 扫描启动：ProjectId={ProjectId}, Mode={Mode}, PatternCount={PatternCount}, TotalFrameCount={TotalFrameCount}",
            project.Id,
            mode,
            patternCount,
            totalFrameCount
        );

        CalibScanStatusDto status = ToStatusDto(session);
        await _scanNotifier.NotifyStateAsync(status);
        return status;
    }

    /// <summary>
    /// 扫描循环主逻辑。
    /// 含投影仪模式：每轮 T 指令触发首轮，N 指令推进剩余帧，最后十字图检测确认。
    /// 无投影仪模式：直接循环抓拍主从相机推送。
    /// </summary>
    /// <param name="totalFrameCount">每轮总帧数（含横竖两个方向，即 2×PatternCount）</param>
    /// <param name="patternCount">每个方向的相移步数</param>
    private async Task ScanLoopAsync(
        CalibProject project,
        CalibScanMode mode,
        int totalFrameCount,
        int patternCount,
        IDlpProjectorService? projectorService,
        CancellationToken cancellationToken
    )
    {
        long roundIndex = 0;
        DateTime lastMetricAt = DateTime.UtcNow;

        while (!cancellationToken.IsCancellationRequested)
        {
            roundIndex++;
            int frameIndexInRound = 0;

            if (projectorService != null && totalFrameCount > 0)
            {
                // 含投影仪模式：结构光扫描流程
                // 第 1 张：T 指令触发
                await SafeProjectorInvokeAsync(
                    projectorService,
                    svc => svc.TriggerOnceAsync(),
                    "TriggerOnceAsync(T)"
                );
                if (cancellationToken.IsCancellationRequested)
                    break;

                await CaptureAndPushFrameAsync(
                    project,
                    mode,
                    roundIndex,
                    frameIndexInRound,
                    totalFrameCount,
                    cancellationToken
                );
                lastMetricAt = await PushMetricsAsync(
                    project.Id,
                    roundIndex,
                    frameIndexInRound,
                    totalFrameCount,
                    false,
                    lastMetricAt
                );
                frameIndexInRound++;

                // 第 2~TotalFrameCount 张：N 指令推进
                for (int i = 1; i < totalFrameCount; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    await SafeProjectorInvokeAsync(
                        projectorService,
                        svc => svc.NextFrameAsync(),
                        "NextFrameAsync(N)"
                    );
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    await CaptureAndPushFrameAsync(
                        project,
                        mode,
                        roundIndex,
                        frameIndexInRound,
                        totalFrameCount,
                        cancellationToken
                    );
                    lastMetricAt = await PushMetricsAsync(
                        project.Id,
                        roundIndex,
                        frameIndexInRound,
                        totalFrameCount,
                        false,
                        lastMetricAt
                    );
                    frameIndexInRound++;
                }

                if (cancellationToken.IsCancellationRequested)
                    break;

                // 混合模式：抓一帧做十字图检测确认
                bool isCrosshairDetected = false;
                if (project.MainCameraDeviceId.HasValue)
                {
                    byte[]? confirmBytes = await GrabScanFrameAsync(
                        project.MainCameraDeviceId.Value
                    );
                    if (confirmBytes != null)
                    {
                        isCrosshairDetected = DetectCrosshair(confirmBytes);
                    }
                }

                if (!isCrosshairDetected)
                {
                    _logger.LogWarning(
                        "Step6 轮次 {RoundIndex} 十字图未检测到，可能存在同步问题，继续下一轮",
                        roundIndex
                    );
                }

                CalibScanMetricsDto finalMetrics = new()
                {
                    Fps = 0,
                    DepthValidRate = 0,
                    Confidence = 0,
                    DepthMapDataUri = null,
                    FrameIndex = roundIndex * totalFrameCount + totalFrameCount - 1,
                    Timestamp = DateTime.UtcNow,
                    RoundIndex = roundIndex,
                    FrameIndexInRound = totalFrameCount - 1,
                    PatternCount = totalFrameCount,
                    IsCrosshairDetected = isCrosshairDetected,
                };
                _stateStore.UpdateMetrics(project.Id, finalMetrics);
                await _scanNotifier.NotifyMetricsAsync(project.Id, finalMetrics);

                _ = _pointCloudAppService.GenerateIncrementalPointCloudAsync(
                    project.Id,
                    roundIndex,
                    patternCount
                );
            }
            else
            {
                // 无投影仪模式（TwoCamera0Light）：简单双目抓拍推送
                await CaptureAndPushFrameAsync(
                    project,
                    mode,
                    roundIndex,
                    frameIndexInRound,
                    1,
                    cancellationToken
                );
                lastMetricAt = await PushMetricsAsync(
                    project.Id,
                    roundIndex,
                    0,
                    0,
                    false,
                    lastMetricAt
                );

                // 节流：无投影仪模式无 T/N 指令节拍，需要适度延迟避免压满 USB
                try
                {
                    await Task.Delay(500, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// 抓拍主从相机并推送原图到前端。
    /// 遵循 _capStartActiveLock 顺序单活采集：主相机先拍完，从相机再拍。
    /// 同时保存图像到 Blob 存储，供后续点云合成使用。
    /// </summary>
    private async Task CaptureAndPushFrameAsync(
        CalibProject project,
        CalibScanMode mode,
        long roundIndex,
        int frameIndexInRound,
        int totalFrameCount,
        CancellationToken cancellationToken
    )
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        long accumulatedFrameCount = (roundIndex - 1) * totalFrameCount + frameIndexInRound;

        // 主相机抓拍
        if (project.MainCameraDeviceId.HasValue)
        {
            byte[]? mainBytes = await GrabScanFrameAsync(project.MainCameraDeviceId.Value);
            if (mainBytes != null)
            {
                await _scanNotifier.NotifyFrameAsync(
                    project.Id,
                    (int)CalibScanCameraRole.Main,
                    mainBytes,
                    roundIndex,
                    frameIndexInRound,
                    totalFrameCount,
                    accumulatedFrameCount,
                    false
                );

                await SaveScanImageToBlobAsync(
                    project.Id,
                    project.MainCameraDeviceId.Value,
                    mainBytes,
                    roundIndex,
                    frameIndexInRound,
                    CalibScanCameraRole.Main
                );
            }
        }

        if (cancellationToken.IsCancellationRequested)
            return;

        // 从相机抓拍（双目模式）
        if (
            mode is CalibScanMode.TwoCamera0Light or CalibScanMode.TwoCamera1Light
            && project.SecondaryCameraDeviceId.HasValue
        )
        {
            byte[]? secondaryBytes = await GrabScanFrameAsync(
                project.SecondaryCameraDeviceId.Value
            );
            if (secondaryBytes != null)
            {
                await _scanNotifier.NotifyFrameAsync(
                    project.Id,
                    (int)CalibScanCameraRole.Secondary,
                    secondaryBytes,
                    roundIndex,
                    frameIndexInRound,
                    totalFrameCount,
                    accumulatedFrameCount,
                    false
                );

                await SaveScanImageToBlobAsync(
                    project.Id,
                    project.SecondaryCameraDeviceId.Value,
                    secondaryBytes,
                    roundIndex,
                    frameIndexInRound,
                    CalibScanCameraRole.Secondary
                );
            }
        }
    }

    /// <summary>
    /// 保存扫描图像到 Blob 存储。
    /// </summary>
    private async Task SaveScanImageToBlobAsync(
        Guid projectId,
        Guid cameraDeviceId,
        byte[] imageBytes,
        long roundIndex,
        int frameIndexInRound,
        CalibScanCameraRole cameraRole
    )
    {
        try
        {
            string blobKey = BuildScanBlobKey(
                projectId,
                cameraDeviceId,
                roundIndex,
                frameIndexInRound,
                cameraRole
            );
            await _blobContainer.SaveAsync(blobKey, imageBytes, overrideExisting: true);
            _logger.LogDebug(
                "Step6 扫描图像已保存：ProjectId={ProjectId}, CameraRole={CameraRole}, Round={Round}, Frame={Frame}, Key={Key}",
                projectId,
                cameraRole,
                roundIndex,
                frameIndexInRound,
                blobKey
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Step6 扫描图像保存失败：ProjectId={ProjectId}", projectId);
        }
    }

    /// <summary>
    /// 构建扫描图像的 Blob Key。
    /// </summary>
    public static string BuildScanBlobKey(
        Guid projectId,
        Guid cameraDeviceId,
        long roundIndex,
        int frameIndexInRound,
        CalibScanCameraRole cameraRole
    )
    {
        string roleStr = cameraRole == CalibScanCameraRole.Main ? "main" : "secondary";
        return $"{projectId}/{cameraDeviceId}/scan/{roleStr}_r{roundIndex:0000}_f{frameIndexInRound:000}.jpg";
    }

    /// <summary>
    /// 推送实时指标到前端并更新会话状态。
    /// 返回本次指标的时间戳，调用方将其作为下一次调用的 lastMetricAt 传入。
    /// </summary>
    private async Task<DateTime> PushMetricsAsync(
        Guid projectId,
        long roundIndex,
        int frameIndexInRound,
        int patternCount,
        bool isCrosshairDetected,
        DateTime lastMetricAt
    )
    {
        DateTime now = DateTime.UtcNow;
        double elapsedSeconds = Math.Max((now - lastMetricAt).TotalSeconds, 1e-3);
        double fps = Math.Round(1d / elapsedSeconds, 2);

        CalibScanMetricsDto metrics = new()
        {
            Fps = fps,
            DepthValidRate = 0,
            Confidence = 0,
            DepthMapDataUri = null,
            FrameIndex = (roundIndex - 1) * Math.Max(patternCount, 1) + frameIndexInRound,
            Timestamp = now,
            RoundIndex = roundIndex,
            FrameIndexInRound = frameIndexInRound,
            PatternCount = patternCount,
            IsCrosshairDetected = isCrosshairDetected,
        };

        _stateStore.UpdateMetrics(projectId, metrics);
        await _scanNotifier.NotifyMetricsAsync(projectId, metrics);
        return now;
    }

    /// <summary>
    /// Step6 扫描帧抓取：软件触发 + 应用 ImageRotationAngle 旋转。
    /// 遵循测试结论（TucamMultiCameraProbe）：Cap_Start → 软件触发 → WaitForFrame → Cap_Stop，
    /// _capStartActiveLock 自动保证同一时刻仅一台相机处于活跃采集状态。
    /// </summary>
    private async Task<byte[]?> GrabScanFrameAsync(Guid cameraDeviceId)
    {
        CameraDevice camera;
        try
        {
            camera = await _cameraDeviceRepository.GetAsync(cameraDeviceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Step6 扫描帧抓取：相机设备 {Id} 不存在", cameraDeviceId);
            return null;
        }

        int idx = camera.DeviceIndex;
        if (!_tucamService.IsCameraOpen(idx))
        {
            _logger.LogDebug("Step6 扫描帧抓取：相机 {Index} 未打开，跳过", idx);
            return null;
        }

        try
        {
            await _tucamService.StartCaptureAsync(idx);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Step6 扫描帧抓取：相机 {Index} 启动采集失败", idx);
            return null;
        }

        try
        {
            // 软件触发模式（TriggerMode=2）需要手动发送触发脉冲
            long triggerMode = 0;
            try
            {
                triggerMode = await _tucamService.GetGenICamIntAsync(idx, "TriggerMode");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "Step6 扫描帧抓取：相机 {Index} 读取触发模式失败，按自由运行处理",
                    idx
                );
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
                _logger.LogDebug(
                    ex,
                    "Step6 扫描帧抓取：相机 {Index} 读取曝光时间失败，使用默认超时 {TimeoutMs}ms",
                    idx,
                    timeoutMs
                );
            }

            byte[] jpegBytes = await _tucamService.GrabFrameRawAsync(
                idx,
                timeoutMs,
                maxWidth: 0,
                jpegQuality: 85,
                imageRotationAngle: camera.ImageRotationAngle
            );
            return jpegBytes;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Step6 扫描帧抓取：相机 {Index} 抓帧失败", idx);
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
                _logger.LogWarning(ex, "Step6 扫描帧抓取：相机 {Index} 停止采集失败", idx);
            }
        }
    }

    /// <summary>
    /// 使用 OpenCV 检测图像是否为十字图（白色背景 + 黑色十字）。
    /// 算法：灰度化 → 整体亮度校验 → 自适应阈值二值化 → 中心行/列暗像素比例判定。
    /// </summary>
    /// <param name="jpegBytes">JPEG 图像字节数组</param>
    /// <returns>true 表示检测到十字图</returns>
    private static bool DetectCrosshair(byte[] jpegBytes)
    {
        using Mat gray = Cv2.ImDecode(jpegBytes, ImreadModes.Grayscale);
        if (gray.Empty())
        {
            return false;
        }

        // 整体亮度校验：白色背景应较亮
        double meanBrightness = Cv2.Mean(gray).Val0;
        if (meanBrightness < 100)
        {
            return false;
        }

        // 自适应阈值二值化（应对光照不均），BinaryInv 使十字线（暗）变为白色（255）
        // 参数顺序：src, dst, maxValue, adaptiveMethod, thresholdType, blockSize, C
        using Mat binary = new();
        Cv2.AdaptiveThreshold(
            gray,
            binary,
            255,
            AdaptiveThresholdTypes.GaussianC,
            ThresholdTypes.BinaryInv,
            51,
            10
        );

        int h = gray.Rows;
        int w = gray.Cols;
        int cy = h / 2;
        int cx = w / 2;

        // 统计中心行（水平线）和中心列（垂直线）的暗像素比例
        int rowDark = 0;
        int colDark = 0;
        for (int x = 0; x < w; x++)
        {
            if (binary.At<byte>(cy, x) > 0)
                rowDark++;
        }
        for (int y = 0; y < h; y++)
        {
            if (binary.At<byte>(y, cx) > 0)
                colDark++;
        }

        double rowRatio = (double)rowDark / w;
        double colRatio = (double)colDark / h;

        // 中心十字线暗像素比例 > 60% 判定为十字图
        return rowRatio > 0.6 && colRatio > 0.6;
    }

    /// <inheritdoc/>
    public async Task<CalibScanStatusDto> StopAsync(StopCalibScanInput input)
    {
        CalibProject project = await _projectRepository.GetAsync(input.CalibProjectId);
        CalibScanSessionState? stopped = _stateStore.Stop(project.Id);

        // 投影仪清理：关灯（LL）
        if (project.BoundProjectorDeviceId.HasValue)
        {
            IDlpProjectorService? svc = _projectorConnectionPool.TryGet(
                project.BoundProjectorDeviceId.Value
            );
            if (svc != null)
            {
                await SafeProjectorInvokeAsync(svc, s => s.LedOffAsync(), "LedOffAsync");
            }
        }

        // 相机清理：停止主从相机采集（容错）
        await SafeStopCameraAsync(project.MainCameraDeviceId);
        await SafeStopCameraAsync(project.SecondaryCameraDeviceId);

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

        _ = _pointCloudAppService.CompleteIncrementalPointCloudAsync(project.Id);

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

    /// <summary>
    /// 查询指定项目的 Step3 投影仪参数。
    /// </summary>
    private async Task<CalibProjectorParam> GetProjectorParamAsync(CalibProject project)
    {
        if (!project.BoundProjectorDeviceId.HasValue)
        {
            throw new UserFriendlyException("当前项目未绑定投影仪，请先在 Step2 绑定");
        }

        IQueryable<CalibProjectorParam> query = await _projectorParamRepository.GetQueryableAsync();
        CalibProjectorParam? param = await AsyncExecuter.FirstOrDefaultAsync(
            query.Where(x => x.CalibProjectId == project.Id)
        );

        if (param == null)
        {
            throw new UserFriendlyException("请先在 Step3 完成投影仪参数配置");
        }

        return param;
    }

    /// <summary>
    /// 设置指定相机为软件触发模式（TriggerSource=Software=1, TriggerMode=On=2）。
    /// </summary>
    private async Task EnsureSoftwareTriggerModeAsync(Guid cameraDeviceId)
    {
        CameraDevice camera = await _cameraDeviceRepository.GetAsync(cameraDeviceId);
        int idx = camera.DeviceIndex;

        if (!_tucamService.IsCameraOpen(idx))
        {
            throw new UserFriendlyException($"相机 {idx} 未打开，请先在设备管理页打开相机");
        }

        try
        {
            // 先设置触发源为软件（1=Software），再启用触发模式（2=On）
            await _tucamService.SetGenICamIntAsync(idx, "TriggerSource", 1);
            await _tucamService.SetGenICamIntAsync(idx, "TriggerMode", 2);
            _logger.LogInformation("Step6 相机 {Index} 已切换到软件触发模式", idx);
        }
        catch (Exception ex)
        {
            throw new UserFriendlyException($"相机 {idx} 切换软件触发模式失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 安全调用投影仪指令，捕获异常并记录日志，不中断扫描流程。
    /// </summary>
    private async Task SafeProjectorInvokeAsync(
        IDlpProjectorService svc,
        Func<IDlpProjectorService, Task<bool>> action,
        string operationName
    )
    {
        try
        {
            bool ok = await action(svc);
            if (!ok)
            {
                _logger.LogWarning("Step6 投影仪指令 {Op} 返回 false", operationName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Step6 投影仪指令 {Op} 异常", operationName);
        }
    }

    /// <summary>
    /// 安全停止相机采集（容错）。
    /// </summary>
    private async Task SafeStopCameraAsync(Guid? cameraDeviceId)
    {
        if (!cameraDeviceId.HasValue)
            return;
        try
        {
            CameraDevice camera = await _cameraDeviceRepository.GetAsync(cameraDeviceId.Value);
            if (_tucamService.IsCameraOpen(camera.DeviceIndex))
            {
                await _tucamService.StopCaptureAsync(camera.DeviceIndex);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Step6 停止相机 {Id} 采集失败", cameraDeviceId);
        }
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
