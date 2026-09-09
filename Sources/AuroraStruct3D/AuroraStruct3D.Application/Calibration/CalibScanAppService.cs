using AuroraStruct3D.Calibration.Dtos;
using AuroraStruct3D.Cameras;
using AuroraStruct3D.Projectors;
using AuroraStruct3D.Cameras.Tucam;
using AuroraStruct3D.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.IO.Compression;
using System.Text.Json;
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
    // HID 写入成功并不代表 DLP 已完成换帧。120 fps 只是光机刷新率，
    // 控制板解析命令、切换序列还需要额外时间；过短会把默认十字图当成条纹帧。
    private const int TriggerModeSettleDelayMs = 100;
    private const int TextureFrameSettleDelayMs = 80;

    private readonly IRepository<CalibProject, Guid> _projectRepository;
    private readonly CalibScanStateStore _stateStore;
    private readonly ICalibScanNotifier _scanNotifier;
    private readonly IRepository<CameraDevice, Guid> _cameraDeviceRepository;
    private readonly ICameraDriverRegistry _cameraDrivers;
    private ITucamCameraService _tucamService =>
        _cameraDrivers.GetRequired("tucam") as ITucamCameraService
        ?? throw new UserFriendlyException("Tucam 驱动不可用");
    private readonly IRepository<CalibProjectorParam, Guid> _projectorParamRepository;
    private readonly IProjectorConnectionPool _projectorConnectionPool;
    private readonly ILogger<CalibScanAppService> _logger;
    private readonly IBlobContainer<CalibPhotoBlobContainer> _blobContainer;
    private readonly ICalibPointCloudAppService _pointCloudAppService;
    private readonly CalibPointCloudStateStore _pointCloudStateStore;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IDeviceOperationSessionManager _deviceSessions;

    /// <inheritdoc/>
    public async Task<byte[]> DownloadRoundImagesAsync(Guid calibProjectId, long roundIndex)
    {
        if (roundIndex <= 0)
            throw new UserFriendlyException("扫描轮次必须从 1 开始");

        CalibProject project = await _projectRepository.GetAsync(calibProjectId);
        if (!project.MainCameraDeviceId.HasValue)
            throw new UserFriendlyException("标定项目尚未绑定主相机");

        CalibScanMode mode = ResolveScanMode(project);
        int frameCount = 1;
        int periodCount = 0;
        int phaseCount = 0;
        if (mode is CalibScanMode.OneCamera1Light or CalibScanMode.TwoCamera1Light)
        {
            CalibProjectorParam param = await GetProjectorParamAsync(project);
            periodCount = param.PeriodCount;
            phaseCount = param.PatternCount;
            frameCount = GrayPhasePatternLayout.GetTotalFrameCount(periodCount, phaseCount);
        }

        using MemoryStream output = new();
        int imageCount = 0;
        List<object> entries = [];
        using (ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            async Task AddFrameAsync(Guid cameraId, CalibScanCameraRole role, int frameIndex)
            {
                string blobKey = BuildScanBlobKey(
                    project.Id, cameraId, roundIndex, frameIndex, role);
                if (!await _blobContainer.ExistsAsync(blobKey)) return;

                byte[] bytes = await _blobContainer.GetAllBytesAsync(blobKey);
                string roleName = role == CalibScanCameraRole.Main ? "main" : "secondary";
                string extension = DetectImageExtension(bytes);
                string fileName = $"{roleName}/{roleName}_r{roundIndex:0000}_f{frameIndex:000}{extension}";
                ZipArchiveEntry entry = archive.CreateEntry(fileName, CompressionLevel.Fastest);
                await using Stream stream = entry.Open();
                await stream.WriteAsync(bytes);
                imageCount++;
                entries.Add(new
                {
                    fileName,
                    cameraRole = roleName,
                    frameIndex,
                    label = periodCount > 0
                        ? GrayPhasePatternLayout.GetFrameLabel(frameIndex, periodCount, phaseCount)
                        : "普通双目帧",
                    byteLength = bytes.Length,
                });
            }

            for (int frame = 0; frame < frameCount; frame++)
            {
                await AddFrameAsync(project.MainCameraDeviceId.Value, CalibScanCameraRole.Main, frame);
                if (project.SecondaryCameraDeviceId.HasValue)
                    await AddFrameAsync(project.SecondaryCameraDeviceId.Value,
                        CalibScanCameraRole.Secondary, frame);
            }

            string textureKey = BuildTextureBlobKey(
                project.Id, project.MainCameraDeviceId.Value, roundIndex);
            if (await _blobContainer.ExistsAsync(textureKey))
            {
                byte[] bytes = await _blobContainer.GetAllBytesAsync(textureKey);
                string fileName = $"main/main_r{roundIndex:0000}_texture{DetectImageExtension(bytes)}";
                ZipArchiveEntry entry = archive.CreateEntry(fileName, CompressionLevel.Fastest);
                await using Stream stream = entry.Open();
                await stream.WriteAsync(bytes);
                imageCount++;
                entries.Add(new { fileName, cameraRole = "main", frameIndex = -1,
                    label = "白光纹理帧", byteLength = bytes.Length });
            }

            if (imageCount == 0)
                throw new UserFriendlyException($"第 {roundIndex} 轮没有可下载的扫描图片");

            ZipArchiveEntry manifestEntry = archive.CreateEntry("manifest.json");
            await using Stream manifestStream = manifestEntry.Open();
            await JsonSerializer.SerializeAsync(manifestStream, new
            {
                formatVersion = 1,
                calibProjectId = project.Id,
                roundIndex,
                scanMode = mode.ToString(),
                periodCount,
                phaseCount,
                expectedPatternFrameCount = frameCount,
                imageCount,
                entries,
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        return output.ToArray();
    }

    private static string DetectImageExtension(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M') return ".bmp";
        if (bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(
                new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return ".png";
        return ".jpg";
    }

    /// <summary>构造注入</summary>
    public CalibScanAppService(
        IRepository<CalibProject, Guid> projectRepository,
        CalibScanStateStore stateStore,
        ICalibScanNotifier scanNotifier,
        IRepository<CameraDevice, Guid> cameraDeviceRepository,
        ICameraDriverRegistry cameraDrivers,
        IRepository<CalibProjectorParam, Guid> projectorParamRepository,
        IProjectorConnectionPool projectorConnectionPool,
        ILogger<CalibScanAppService> logger,
        IBlobContainer<CalibPhotoBlobContainer> blobContainer,
        ICalibPointCloudAppService pointCloudAppService,
        CalibPointCloudStateStore pointCloudStateStore,
        IServiceScopeFactory serviceScopeFactory,
        IDeviceOperationSessionManager deviceSessions
    )
    {
        _projectRepository = projectRepository;
        _stateStore = stateStore;
        _scanNotifier = scanNotifier;
        _cameraDeviceRepository = cameraDeviceRepository;
        _cameraDrivers = cameraDrivers;
        _projectorParamRepository = projectorParamRepository;
        _projectorConnectionPool = projectorConnectionPool;
        _logger = logger;
        _blobContainer = blobContainer;
        _pointCloudAppService = pointCloudAppService;
        _pointCloudStateStore = pointCloudStateStore;
        _serviceScopeFactory = serviceScopeFactory;
        _deviceSessions = deviceSessions;
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
        _pointCloudStateStore.ConfigureTableFilter(
            input.CalibProjectId,
            input.EnableTableFilter,
            input.TableClearanceMm
        );
        CalibScanMode mode = ResolveScanMode(project);

        ValidateBindingsForScanMode(project, mode);
        string hardwareSessionId = BuildHardwareSessionId(project.Id);
        AcquireHardwareSessions(project, mode, hardwareSessionId);
        using PreviewHardwareLeaseGuard startupLease = new(() =>
            ReleaseHardwareSessions(project, mode, hardwareSessionId)
        );
        bool suppressProjectorControl =
            input.SuppressProjectorControl
            && mode is not (CalibScanMode.OneCamera1Light or CalibScanMode.TwoCamera1Light);
        if (input.SuppressProjectorControl && !suppressProjectorControl)
        {
            _logger.LogWarning(
                "Step6 已忽略屏蔽投影仪控制参数：ProjectId={ProjectId}, Mode={Mode}。结构光模式必须控制投影仪。",
                project.Id,
                mode
            );
        }

        // PatternCount 是 Step3 配置的每方向图像数，一轮依次采集横、竖两个方向。
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

            // 结构光由投影仪 TRIG_OUT 同时触发相机，必须使用 Standard 外触发模式。
            await EnsureHardwareTriggerModeAsync(project.MainCameraDeviceId!.Value);
            if (project.SecondaryCameraDeviceId.HasValue)
            {
                await EnsureHardwareTriggerModeAsync(project.SecondaryCameraDeviceId.Value);
            }

            if (!suppressProjectorControl)
            {
                CalibProjectorParam param = await GetProjectorParamAsync(project);
                patternCount = param.PatternCount;
                if (patternCount < 3)
                {
                    throw new UserFriendlyException("每个方向至少需要 3 张相移条纹图像");
                }
                totalFrameCount = GrayPhasePatternLayout.GetTotalFrameCount(
                    param.PeriodCount,
                    patternCount
                );
                projectorService = connectedProjector;

                await RequireProjectorInvokeAsync(
                    connectedProjector,
                    svc => svc.SetBootImageAsync(ProjectorBootImage.Cross),
                    "SetBootImageAsync(Cross)"
                );
                await RequireProjectorInvokeAsync(
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
        if (
            (mode is CalibScanMode.TwoCamera0Light or CalibScanMode.TwoCamera1Light)
            && project.SecondaryCameraDeviceId.HasValue
        )
        {
            // A scan is a new accumulation boundary. Do not reuse chunks left by a
            // failed/stopped previous run merely because its point-cloud session still exists.
            _pointCloudStateStore.StartIncrementalMode(project.Id);
        }

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
                try
                {
                    await scopedService.ScanLoopAsync(
                        project,
                        mode,
                        totalFrameCount,
                        patternCount,
                        projectorService,
                        ct
                    );
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    scopedService._logger.LogError(
                        ex,
                        "Step6 扫描循环失败：ProjectId={ProjectId}",
                        project.Id
                    );
                    CalibScanSessionState failed = scopedService._stateStore.Fail(
                        project.Id,
                        ex.Message
                    );
                    await scopedService._scanNotifier.NotifyStateAsync(ToStatusDto(failed));
                    throw;
                }
                finally
                {
                    try
                    {
                        await scopedService.CleanupScanHardwareAsync(project);
                    }
                    finally
                    {
                        scopedService.ReleaseHardwareSessions(
                            project,
                            mode,
                            hardwareSessionId
                        );
                    }
                }
            }
        );
        startupLease.TransferToScanLoop();

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
    /// <param name="totalFrameCount">每轮总帧数；等于横、竖两组的配置帧数之和</param>
    /// <param name="patternCount">Step3 配置的每方向条纹帧数</param>
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
                // 投影仪断电重启后可能把 MA 播放范围恢复为旧的 16 帧。
                // 每轮先恢复与当前 Gray+相移布局一致的范围和横竖方向，避免第 17 次 N
                // 指令没有同步脉冲而令两台相机一起 WaitForFrame 超时。
                await projectorService.ConfigureFringePlaybackAsync(
                    totalFrameCount,
                    totalFrameCount / 2,
                    cancellationToken
                );

                // 每轮都执行 B0 -> 等待 2 秒 -> B2，清除可能残留的旧触发状态，
                // 确保随后 T 显示首帧、N 单帧推进。
                await EnterSingleFrameTriggerModeAsync(projectorService, cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                    break;

                // 白光纹理帧会把主相机临时切回 FreeRunning，因此每轮重新确认两台相机
                // 均为 Standard 外触发，再让它们同时保持 Cap_Start(TriggerStandard)。
                await EnsureHardwareTriggerModeAsync(project.MainCameraDeviceId!.Value);
                if (project.SecondaryCameraDeviceId.HasValue)
                    await EnsureHardwareTriggerModeAsync(project.SecondaryCameraDeviceId.Value);

                await using TucamHardwareTriggerCaptureSession triggerSession =
                    await CreateHardwareTriggerSessionAsync(project, mode, cancellationToken);

                for (int i = 0; i < totalFrameCount; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    bool firstFrame = i == 0;
                    ScanFrameQualityPair frameQuality =
                        await CaptureAndPushHardwareTriggeredFrameAsync(
                            triggerSession,
                            ct =>
                                RequireProjectorInvokeAsync(
                                    projectorService,
                                    svc =>
                                        firstFrame
                                            ? svc.TriggerOnceAsync(ct)
                                            : svc.NextFrameAsync(ct),
                                    firstFrame ? "TriggerOnceAsync(T)" : "NextFrameAsync(N)"
                                ),
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
                        lastMetricAt,
                        frameQuality
                    );
                    frameIndexInRound++;
                }

                if (cancellationToken.IsCancellationRequested)
                    break;

                // 离开 using 前显式停止双相机硬触发会话，随后才能切换主相机为
                // FreeRunning 拍摄白光纹理；DisposeAsync 是幂等的。
                await triggerSession.DisposeAsync();

                // 条纹序列完成后切换白屏，仅由主相机采集一张独立纹理帧。
                // 纹理帧不参与相位计算，只用于为重建点云采样真实 RGB。
                await SafeProjectorInvokeAsync(
                    projectorService,
                    svc => svc.SetDisplayModeAsync(ProjectorDisplayMode.White),
                    "SetDisplayModeAsync(White)"
                );
                await EnsureFreeRunningModeAsync(project.MainCameraDeviceId!.Value);
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
                ScanFrameQualityPair frameQuality = await CaptureAndPushFrameAsync(
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
                    lastMetricAt,
                    frameQuality
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
    /// 两台相机先并行进入 WaitForFrame，再由投影仪的一次 T/N 硬件沿同步曝光。
    /// 同时保存图像到 Blob 存储，供后续点云合成使用。
    /// </summary>
    private async Task<ScanFrameQualityPair> CaptureAndPushHardwareTriggeredFrameAsync(
        TucamHardwareTriggerCaptureSession triggerSession,
        Func<CancellationToken, Task> triggerAsync,
        CalibProject project,
        CalibScanMode mode,
        long roundIndex,
        int frameIndexInRound,
        int totalFrameCount,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<byte[]> frames = await triggerSession.CaptureAsync(
            triggerAsync,
            cancellationToken
        );
        byte[]? mainBytes = frames.Count > 0 ? frames[0] : null;
        byte[]? secondaryBytes = frames.Count > 1 ? frames[1] : null;
        return await PublishCapturedFramesAsync(
            project,
            mode,
            mainBytes,
            secondaryBytes,
            roundIndex,
            frameIndexInRound,
            totalFrameCount,
            cancellationToken
        );
    }

    /// <summary>
    /// 无投影仪模式使用原有的主→从短会话抓拍。
    /// </summary>
    private async Task<ScanFrameQualityPair> CaptureAndPushFrameAsync(
        CalibProject project,
        CalibScanMode mode,
        long roundIndex,
        int frameIndexInRound,
        int totalFrameCount,
        CancellationToken cancellationToken
    )
    {
        if (cancellationToken.IsCancellationRequested)
            return default;

        byte[]? mainBytes = project.MainCameraDeviceId.HasValue
            ? await GrabScanFrameAsync(project.MainCameraDeviceId.Value)
            : null;
        byte[]? secondaryBytes = null;
        if (
            !cancellationToken.IsCancellationRequested
            && mode is CalibScanMode.TwoCamera0Light or CalibScanMode.TwoCamera1Light
            && project.SecondaryCameraDeviceId.HasValue
        )
        {
            secondaryBytes = await GrabScanFrameAsync(project.SecondaryCameraDeviceId.Value);
        }

        return await PublishCapturedFramesAsync(
            project,
            mode,
            mainBytes,
            secondaryBytes,
            roundIndex,
            frameIndexInRound,
            totalFrameCount,
            cancellationToken
        );
    }

    private async Task<ScanFrameQualityPair> PublishCapturedFramesAsync(
        CalibProject project,
        CalibScanMode mode,
        byte[]? mainBytes,
        byte[]? secondaryBytes,
        long roundIndex,
        int frameIndexInRound,
        int totalFrameCount,
        CancellationToken cancellationToken
    )
    {
        ScanFrameExposureMetrics? mainQuality = null;
        ScanFrameExposureMetrics? secondaryQuality = null;
        long accumulatedFrameCount = (roundIndex - 1) * totalFrameCount + frameIndexInRound;

        if (mainBytes != null && project.MainCameraDeviceId.HasValue)
        {
            mainQuality = ScanFrameExposureAnalyzer.Analyze(mainBytes);
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

        if (cancellationToken.IsCancellationRequested)
            return new ScanFrameQualityPair(mainQuality, secondaryQuality);

        if (
            secondaryBytes != null
            &&
            mode is CalibScanMode.TwoCamera0Light or CalibScanMode.TwoCamera1Light
            && project.SecondaryCameraDeviceId.HasValue
        )
        {
            secondaryQuality = ScanFrameExposureAnalyzer.Analyze(secondaryBytes);
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

        return new ScanFrameQualityPair(mainQuality, secondaryQuality);
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
        DateTime lastMetricAt,
        ScanFrameQualityPair frameQuality
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
            MainExposure = ToExposureDto(frameQuality.Main),
            SecondaryExposure = ToExposureDto(frameQuality.Secondary),
        };

        LogExposure(projectId, roundIndex, frameIndexInRound, "Main", frameQuality.Main);
        LogExposure(projectId, roundIndex, frameIndexInRound, "Secondary", frameQuality.Secondary);

        _stateStore.UpdateMetrics(projectId, metrics);
        await _scanNotifier.NotifyMetricsAsync(projectId, metrics);
        return now;
    }

    private static CalibScanExposureMetricsDto? ToExposureDto(ScanFrameExposureMetrics? value) =>
        value is { } v ? new CalibScanExposureMetricsDto
        {
            SaturatedRatio = v.SaturatedRatio,
            CrushedRatio = v.CrushedRatio,
            P01 = v.P01,
            P50 = v.P50,
            P99 = v.P99,
        } : null;

    private void LogExposure(Guid projectId, long round, int frame, string role,
        ScanFrameExposureMetrics? value)
    {
        if (value is not { } v) return;
        _logger.LogInformation(
            "结构光曝光：ProjectId={ProjectId}, Round={Round}, Frame={Frame}, Camera={Camera}, SaturatedRatio={SaturatedRatio}, CrushedRatio={CrushedRatio}, P01={P01}, P50={P50}, P99={P99}",
            projectId, round, frame, role, v.SaturatedRatio, v.CrushedRatio,
            v.P01, v.P50, v.P99);
    }

    private readonly record struct ScanFrameQualityPair(
        ScanFrameExposureMetrics? Main,
        ScanFrameExposureMetrics? Secondary);

    /// <summary>
    /// Step6 普通双目或白光纹理帧抓取：短生命周期采集并应用 ImageRotationAngle 旋转。
    /// 结构光条纹帧不走此方法，而由 TucamHardwareTriggerCaptureSession 同步抓取。
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

        int idx = ResolveTucamRuntimeIndex(camera);
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

    private async Task CleanupScanHardwareAsync(CalibProject project)
    {
        if (project.BoundProjectorDeviceId.HasValue)
        {
            IDlpProjectorService? projector = _projectorConnectionPool.TryGet(
                project.BoundProjectorDeviceId.Value
            );
            if (projector is not null)
                await SafeProjectorInvokeAsync(
                    projector,
                    service => service.LedOffAsync(),
                    "LedOffAsync"
                );
        }

        await SafeStopCameraAsync(project.MainCameraDeviceId);
        await SafeStopCameraAsync(project.SecondaryCameraDeviceId);
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
        if (param.PatternCount < 3)
        {
            throw new UserFriendlyException(
                $"当前项目每方向条纹数量为 {param.PatternCount}，至少需要 3 张。"
                + "请在 Step3 修正配置并重新下载条纹到投影仪。"
            );
        }
        if (param.ProjectorDeviceId != project.BoundProjectorDeviceId.Value)
        {
            throw new UserFriendlyException("Step3 投影仪配置与当前项目绑定的投影仪不一致，请重新保存配置");
        }
        if (
            param.ResolutionWidth <= 0
            || param.ResolutionHeight <= 0
            || param.PeriodCount <= 0
            || param.ResolutionWidth % param.PeriodCount != 0
            || param.ResolutionHeight % param.PeriodCount != 0
            || !param.PhaseShift.HasValue
            || param.PhaseShift.Value <= 0
        )
        {
            throw new UserFriendlyException("Step3 投影仪条纹配置无效，请重新保存并下载条纹");
        }

        return param;
    }

    private async Task<TucamHardwareTriggerCaptureSession> CreateHardwareTriggerSessionAsync(
        CalibProject project,
        CalibScanMode mode,
        CancellationToken cancellationToken
    )
    {
        var cameras = new List<TucamTriggeredCamera>(2)
        {
            await CreateTriggeredCameraAsync(
                project.MainCameraDeviceId!.Value,
                cancellationToken
            ),
        };
        if (
            mode == CalibScanMode.TwoCamera1Light
            && project.SecondaryCameraDeviceId.HasValue
        )
        {
            cameras.Add(
                await CreateTriggeredCameraAsync(
                    project.SecondaryCameraDeviceId.Value,
                    cancellationToken
                )
            );
        }
        return await TucamHardwareTriggerCaptureSession.StartAsync(cameras, cancellationToken);
    }

    private async Task<TucamTriggeredCamera> CreateTriggeredCameraAsync(
        Guid cameraDeviceId,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        CameraDevice camera = await _cameraDeviceRepository.GetAsync(cameraDeviceId);
        int idx = ResolveTucamRuntimeIndex(camera);
        if (!_tucamService.IsCameraOpen(idx))
            throw new UserFriendlyException($"相机 {idx} 未打开，请先在设备管理页打开相机");

        int timeoutMs = 8000;
        try
        {
            long exposureUs = await _tucamService.GetGenICamIntAsync(idx, "ExposureTime");
            timeoutMs = (int)Math.Clamp(exposureUs / 1000L * 2 + 1000L, 8000L, 30000L);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Step6 相机 {Index} 读取曝光时间失败，硬触发等待使用默认 {TimeoutMs}ms",
                idx,
                timeoutMs
            );
        }

        return new TucamTriggeredCamera(
            _tucamService,
            idx,
            timeoutMs,
            camera.ImageRotationAngle
        );
    }

    /// <summary>设置相机为投影仪 TRIG_OUT 使用的 Standard 外触发模式。</summary>
    private Task EnsureHardwareTriggerModeAsync(Guid cameraDeviceId) =>
        EnsureTriggerModeAsync(cameraDeviceId, expectedMode: 1, "Standard 外触发");

    /// <summary>设置相机为白光纹理抓拍使用的自由运行模式。</summary>
    private Task EnsureFreeRunningModeAsync(Guid cameraDeviceId) =>
        EnsureTriggerModeAsync(cameraDeviceId, expectedMode: 0, "FreeRunning");

    private async Task EnsureTriggerModeAsync(
        Guid cameraDeviceId,
        long expectedMode,
        string modeName
    )
    {
        CameraDevice camera = await _cameraDeviceRepository.GetAsync(cameraDeviceId);
        int idx = ResolveTucamRuntimeIndex(camera);
        if (!_tucamService.IsCameraOpen(idx))
            throw new UserFriendlyException($"相机 {idx} 未打开，请先在设备管理页打开相机");

        try
        {
            if (_tucamService.IsCapturing(idx))
                await _tucamService.StopCaptureAsync(idx);
            await _tucamService.SetGenICamIntAsync(idx, "TriggerMode", expectedMode);
            long actualMode = await _tucamService.GetGenICamIntAsync(idx, "TriggerMode");
            if (actualMode != expectedMode)
                throw new InvalidOperationException(
                    $"TriggerMode 写入 {expectedMode} 后读回 {actualMode}"
                );
            _logger.LogInformation(
                "Step6 相机 {Index} 已切换到 {Mode}（TriggerMode={TriggerMode}）",
                idx,
                modeName,
                expectedMode
            );
        }
        catch (Exception ex)
        {
            throw new UserFriendlyException(
                $"相机 {idx} 切换到 {modeName} 失败：{ex.Message}"
            );
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
            throw new UserFriendlyException($"投影仪必要指令 {operationName} 失败：{ex.Message}");
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
        // B2 命令完成写入后等待固件真正进入单帧状态，再发送 T。
        // 否则 T 可能仍按 B0 处理，序列播放后回落到默认十字图。
        await Task.Delay(TriggerModeSettleDelayMs, cancellationToken);
    }

    /// <summary>
    /// 安全停止相机采集（容错）。
    /// </summary>
    private async Task SafeStopCameraAsync(Guid? cameraDeviceId)
    {
        if (!cameraDeviceId.HasValue)
            return;

        CameraDevice camera;
        int runtimeIndex;
        try
        {
            camera = await _cameraDeviceRepository.GetAsync(cameraDeviceId.Value);
            runtimeIndex = ResolveTucamRuntimeIndex(camera);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Step6 无法解析相机 {Id}，跳过停止与触发模式恢复", cameraDeviceId);
            return;
        }

        try
        {
            if (_tucamService.IsCameraOpen(runtimeIndex))
            {
                await _tucamService.StopCaptureAsync(runtimeIndex);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Step6 停止相机 {Id} 采集失败", cameraDeviceId);
        }

        try
        {
            if (_tucamService.IsCameraOpen(runtimeIndex))
            {
                // 结构光扫描会把相机切到 TriggerMode=On(2)。调试页使用连续流，
                // 因此退出扫描时必须切回 Off(0)，否则预览已启动但相机一直等待软件触发。
                await _tucamService.SetGenICamIntAsync(runtimeIndex, "TriggerMode", 0);
                _logger.LogInformation("Step6 相机 {Index} 已恢复连续采集模式", runtimeIndex);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Step6 恢复相机 {Id} 连续采集模式失败", cameraDeviceId);
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

    private static string BuildHardwareSessionId(Guid projectId) =>
        $"calib-scan:{projectId:N}";

    private sealed class PreviewHardwareLeaseGuard(Action release) : IDisposable
    {
        private int _transferred;

        public void TransferToScanLoop() => Interlocked.Exchange(ref _transferred, 1);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _transferred, 1) == 0)
                release();
        }
    }

    private void AcquireHardwareSessions(
        CalibProject project,
        CalibScanMode mode,
        string sessionId
    )
    {
        List<(Guid Id, DeviceType Type)> devices = GetHardwareDevices(project, mode);
        List<Guid> acquired = [];
        try
        {
            foreach ((Guid id, DeviceType type) in devices)
            {
                _deviceSessions.TryAcquire(
                    id,
                    type,
                    sessionId,
                    userId: null,
                    userName: "标定在线预览",
                    force: false,
                    neverExpire: true
                );
                acquired.Add(id);
            }
        }
        catch
        {
            foreach (Guid id in acquired.AsEnumerable().Reverse())
                _deviceSessions.Release(id, sessionId);
            throw;
        }
    }

    private void ReleaseHardwareSessions(
        CalibProject project,
        CalibScanMode mode,
        string sessionId
    )
    {
        foreach ((Guid id, _) in GetHardwareDevices(project, mode).AsEnumerable().Reverse())
            _deviceSessions.Release(id, sessionId);
    }

    private static List<(Guid Id, DeviceType Type)> GetHardwareDevices(
        CalibProject project,
        CalibScanMode mode
    )
    {
        List<(Guid Id, DeviceType Type)> devices = [];
        if (project.MainCameraDeviceId.HasValue)
            devices.Add((project.MainCameraDeviceId.Value, DeviceType.Camera));
        if (project.SecondaryCameraDeviceId.HasValue)
            devices.Add((project.SecondaryCameraDeviceId.Value, DeviceType.Camera));
        if (
            mode is CalibScanMode.OneCamera1Light or CalibScanMode.TwoCamera1Light
            && project.BoundProjectorDeviceId.HasValue
        )
            devices.Add((project.BoundProjectorDeviceId.Value, DeviceType.Projector));
        return devices
            .Distinct()
            .OrderBy(x => x.Id)
            .ThenBy(x => x.Type)
            .ToList();
    }

    private int ResolveTucamRuntimeIndex(CameraDevice camera)
    {
        if (string.IsNullOrWhiteSpace(camera.HardwareId))
            throw new UserFriendlyException($"相机 [{camera.Name}] 尚未绑定稳定硬件标识");
        ICameraDriver driver = _cameraDrivers.GetRequired(camera.DriverId);
        if (driver is not ITucamCameraService)
            throw new UserFriendlyException(
                $"相机驱动 [{camera.DriverId}] 尚未提供在线标定适配器"
            );
        return driver.TryGetRuntimeIndex(camera.HardwareId, out int index)
            ? index
            : throw new UserFriendlyException($"相机 [{camera.Name}] 当前离线");
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
