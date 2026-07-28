using AuroraStruct3D.Calibration.Dtos;
using AuroraStruct3D.Cameras;
using AuroraStruct3D.Projectors;
using AuroraStruct3D.Tucam;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    private const int PatternFrameSettleDelayMs = 50;
    private const int TextureFrameSettleDelayMs = 80;

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
    private readonly IServiceScopeFactory _serviceScopeFactory;

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
        ICalibPointCloudAppService pointCloudAppService,
        IServiceScopeFactory serviceScopeFactory
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
        _serviceScopeFactory = serviceScopeFactory;
    }

    /// <inheritdoc/>
    public async Task<CalibScanStatusDto> StartAsync(StartCalibScanInput input)
    {
        CalibScanSessionState? existingSession = _stateStore.TryGet(input.CalibProjectId);
        if (existingSession?.IsRunning == true)
        {
            throw new UserFriendlyException("该项目的在线扫描已在运行，请勿重复启动");
        }

        CalibProject project = await _projectRepository.GetAsync(input.CalibProjectId);
        CalibScanMode mode = ResolveScanMode(project);

        ValidateBindingsForScanMode(project, mode);
        bool suppressProjectorControl =
            input.SuppressProjectorControl
            && mode is not (CalibScanMode.OneCamera1Light or CalibScanMode.TwoCamera1Light);
        if (input.SuppressProjectorControl && !suppressProjectorControl)
        {
            _logger.LogWarning(
                "Step6 已忽略屏蔽投影仪控制参数：ProjectId={ProjectId}, Mode={Mode}。当前配置强制使用 20 帧结构光流程。",
                project.Id,
                mode
            );
        }

        // 投影图案固定为 20 帧多尺度互补条纹，数据库中的旧 PatternCount 不再参与帧数计算。
        int patternCount = 0;
        int totalFrameCount = 0;
        IDlpProjectorService? projectorService = null;
        if (mode is CalibScanMode.OneCamera1Light or CalibScanMode.TwoCamera1Light)
        {
            IDlpProjectorService? connectedProjector = _projectorConnectionPool.TryGet(
                project.BoundProjectorDeviceId!.Value
            );
            if (connectedProjector == null)
            {
                throw new UserFriendlyException("投影仪尚未连接，请先在设备管理页连接投影仪");
            }

            await SafeProjectorInvokeAsync(
                connectedProjector,
                svc => svc.LedOnAsync(),
                "LedOnAsync"
            );

            // 设置主从相机为软件触发模式（TriggerSource=Software=1, TriggerMode=On=2）
            await EnsureSoftwareTriggerModeAsync(project.MainCameraDeviceId!.Value);
            if (project.SecondaryCameraDeviceId.HasValue)
            {
                await EnsureSoftwareTriggerModeAsync(project.SecondaryCameraDeviceId.Value);
            }

            if (!suppressProjectorControl)
            {
                await GetProjectorParamAsync(project);
                patternCount = GrayCodePatternLayout.TotalFrameCount;
                totalFrameCount = GrayCodePatternLayout.TotalFrameCount;
                projectorService = connectedProjector;

                await SafeProjectorInvokeAsync(
                    connectedProjector,
                    svc => svc.SetBootImageAsync(ProjectorBootImage.Cross),
                    "SetBootImageAsync(Cross)"
                );
                await SafeProjectorInvokeAsync(
                    connectedProjector,
                    svc => svc.SetDisplayModeAsync(ProjectorDisplayMode.Cross),
                    "SetDisplayModeAsync(Cross)"
                );
                // 扫描启动先退出旧触发状态，等待固件稳定后再进入 B2。
                await EnterSingleFrameTriggerModeAsync(connectedProjector);
            }
            else
            {
                // 普通双目模式每轮只有一组主/从图，状态显示为 0/1。
                totalFrameCount = 1;
            }
        }

        CalibScanSessionState session = _stateStore.Start(project.Id, mode, totalFrameCount);

        // 扫描循环会在当前 HTTP 请求结束后继续运行，不能捕获本请求作用域中的
        // AppService、BlobContainer 或仓储。为整个循环创建独立作用域，停止扫描
        // 并等待循环退出后该作用域才会释放。
        _stateStore.StartScanLoop(
            project.Id,
            async ct =>
            {
                await using AsyncServiceScope scope = _serviceScopeFactory.CreateAsyncScope();
                CalibScanAppService scopedService =
                    scope.ServiceProvider.GetRequiredService<CalibScanAppService>();
                await scopedService.ScanLoopAsync(
                    project,
                    mode,
                    totalFrameCount,
                    patternCount,
                    projectorService,
                    ct
                );
            }
        );

        _logger.LogInformation(
            "Step6 扫描启动：ProjectId={ProjectId}, Mode={Mode}, SuppressProjectorControl={SuppressProjectorControl}, PatternCount={PatternCount}, TotalFrameCount={TotalFrameCount}",
            project.Id,
            mode,
            suppressProjectorControl,
            patternCount,
            totalFrameCount
        );

        CalibScanStatusDto status = ToStatusDto(session);
        await _scanNotifier.NotifyStateAsync(status);
        return status;
    }

    /// <summary>
    /// 扫描循环主逻辑。
    /// 含投影仪模式：每轮 T 指令触发首帧，N 指令推进剩余帧；
    /// 条纹采集结束后直接进入纹理采集与重建，不检测十字图。
    /// 无投影仪模式：直接循环抓拍主从相机推送。
    /// </summary>
    /// <param name="totalFrameCount">每轮总帧数；多尺度互补条纹固定为 20</param>
    /// <param name="patternCount">兼容状态字段，结构光模式下等于总帧数</param>
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
                // 每轮都执行 B0 -> 等待 2 秒 -> B2，清除可能残留的旧触发状态，
                // 确保随后 T 显示首帧、N 单帧推进。
                await EnterSingleFrameTriggerModeAsync(projectorService, cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                    break;

                // 第 1 张：T 指令触发
                await SafeProjectorInvokeAsync(
                    projectorService,
                    svc => svc.TriggerOnceAsync(),
                    "TriggerOnceAsync(T)"
                );
                if (cancellationToken.IsCancellationRequested)
                    break;

                // T 仅表示 HID 命令已写入；等待光机跨过数个 120fps 刷新周期，
                // 避免首张相机图仍抓到进入 B2 前的十字图或上一轮末帧。
                await Task.Delay(PatternFrameSettleDelayMs, cancellationToken);
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

                    await Task.Delay(PatternFrameSettleDelayMs, cancellationToken);
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

                // 条纹序列完成后切换白屏，仅由主相机采集一张独立纹理帧。
                // 纹理帧不参与相位计算，只用于为重建点云采样真实 RGB；
                // 不同时启动主从预览，避免 RK3588 USB 总线被两路连续流占满。
                await SafeProjectorInvokeAsync(
                    projectorService,
                    svc => svc.SetDisplayModeAsync(ProjectorDisplayMode.White),
                    "SetDisplayModeAsync(White)"
                );
                await Task.Delay(TextureFrameSettleDelayMs, cancellationToken);
                await CaptureTextureFrameAsync(
                    project,
                    roundIndex,
                    totalFrameCount,
                    cancellationToken
                );

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
                    IsCrosshairDetected = false,
                };
                _stateStore.UpdateMetrics(project.Id, finalMetrics);
                await _scanNotifier.NotifyMetricsAsync(project.Id, finalMetrics);

                if (mode == CalibScanMode.TwoCamera1Light)
                {
                    // 等待本轮重建完成再进入下一轮，保证采集、Blob 读取和点云累积
                    // 严格串行；同时令 StopAsync 能可靠等待最后一轮完成。
                    await RunPointCloudInNewScopeAsync(
                        service =>
                            service.GenerateIncrementalPointCloudAsync(
                                project.Id,
                                roundIndex,
                                totalFrameCount
                            )
                    );
                }
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
                    1,
                    false,
                    lastMetricAt
                );

                if (
                    mode is CalibScanMode.TwoCamera0Light or CalibScanMode.TwoCamera1Light
                    && project.SecondaryCameraDeviceId.HasValue
                )
                {
                    await RunPointCloudInNewScopeAsync(
                        service =>
                            service.GenerateIncrementalStereoPointCloudAsync(
                                project.Id,
                                roundIndex
                            )
                    );
                }

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
    /// 每轮点云重建使用独立短生命周期 Scope。
    /// 扫描循环会持续多个轮次，不能复用上一轮工作单元结束后已经释放的 EF DbContext。
    /// </summary>
    private async Task RunPointCloudInNewScopeAsync(
        Func<ICalibPointCloudAppService, Task> action)
    {
        await using AsyncServiceScope scope = _serviceScopeFactory.CreateAsyncScope();
        ICalibPointCloudAppService service =
            scope.ServiceProvider.GetRequiredService<ICalibPointCloudAppService>();
        await action(service);
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
    /// 采集当前轮次的主相机白光纹理帧。
    /// 该帧独立保存，不占用条纹帧编号，也不会触发从相机采集。
    /// </summary>
    private async Task CaptureTextureFrameAsync(
        CalibProject project,
        long roundIndex,
        int totalFrameCount,
        CancellationToken cancellationToken
    )
    {
        if (cancellationToken.IsCancellationRequested || !project.MainCameraDeviceId.HasValue)
        {
            return;
        }

        byte[]? textureBytes = await GrabScanFrameAsync(project.MainCameraDeviceId.Value);
        if (textureBytes is null)
        {
            _logger.LogWarning(
                "Step6 白光纹理帧采集失败：ProjectId={ProjectId}, Round={Round}",
                project.Id,
                roundIndex
            );
            return;
        }

        string blobKey = BuildTextureBlobKey(
            project.Id,
            project.MainCameraDeviceId.Value,
            roundIndex
        );
        await _blobContainer.SaveAsync(blobKey, textureBytes, overrideExisting: true);

        // 前端显示最近一次抓拍快照；这里只更新主相机，不增加 USB 采集负载。
        await _scanNotifier.NotifyFrameAsync(
            project.Id,
            (int)CalibScanCameraRole.Main,
            textureBytes,
            roundIndex,
            totalFrameCount,
            totalFrameCount,
            roundIndex * totalFrameCount,
            false
        );
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

    /// <summary>构建主相机白光纹理帧的 Blob Key。</summary>
    public static string BuildTextureBlobKey(
        Guid projectId,
        Guid cameraDeviceId,
        long roundIndex
    )
    {
        return $"{projectId}/{cameraDeviceId}/scan/main_r{roundIndex:0000}_texture.jpg";
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

    /// <inheritdoc/>
    public async Task<CalibScanStatusDto> StopAsync(StopCalibScanInput input)
    {
        CalibProject project = await _projectRepository.GetAsync(input.CalibProjectId);
        CalibScanSessionState? stopped = await _stateStore.StopAsync(project.Id);

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

        await _pointCloudAppService.CompleteIncrementalPointCloudAsync(project.Id);

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
    /// 执行扫描所必需的投影仪指令。失败时立即终止，避免在错误触发模式下继续采集。
    /// </summary>
    private async Task RequireProjectorInvokeAsync(
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
                throw new InvalidOperationException($"投影仪指令 {operationName} 返回失败");
            }

            _logger.LogInformation("Step6 投影仪必要指令执行成功：{Op}", operationName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Step6 投影仪必要指令 {Op} 执行失败，扫描终止", operationName);
            throw new UserFriendlyException($"投影仪切换单帧触发模式 B 2 失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 清除旧触发状态并进入单帧触发模式：B 0，等待 2 秒，再发送 B 2。
    /// </summary>
    private async Task EnterSingleFrameTriggerModeAsync(
        IDlpProjectorService svc,
        CancellationToken cancellationToken = default
    )
    {
        await RequireProjectorInvokeAsync(
            svc,
            projector => projector.SetTriggerModeAsync(
                ProjectorTriggerMode.Normal,
                cancellationToken
            ),
            "SetTriggerModeAsync(Normal/B 0)"
        );
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        await RequireProjectorInvokeAsync(
            svc,
            projector => projector.SetTriggerModeAsync(
                ProjectorTriggerMode.SingleFrame,
                cancellationToken
            ),
            "SetTriggerModeAsync(SingleFrame/B 2)"
        );
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
