using System.Diagnostics;
using System.Runtime.InteropServices;
using AuroraStruct3D.Cameras;
using AuroraStruct3D.Cameras.Tucam;
using AuroraStruct3D.OpenCV.VisionParameters;
using AuroraStruct3D.Projectors;
using AuroraStruct3D.Sessions;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.DependencyInjection;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.Calibration;

/// <summary>生产工作流扫描请求。采集帧和扫描结果均只保存在本次执行内存中。</summary>
public sealed record ProductionPointCloudScanRequest(
    Guid CalibProjectId,
    int CycleCount,
    bool EnableTableFilter = true,
    double TableClearanceMm = 3d
);

/// <summary>生产扫描结果；PointCloud 的所有权移交给工作流上下文。</summary>
public sealed record ProductionPointCloudScanResult(
    PointCloudData PointCloud,
    int PointCount,
    int CompletedCycles,
    long DurationMs
);

/// <summary>由生产扫描算子调用的无状态扫描服务。</summary>
public interface IProductionPointCloudScanService
{
    Task<ProductionPointCloudScanResult> ScanAsync(
        ProductionPointCloudScanRequest request,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// 生产扫描实现。它复用标定项目的双目参数、矫正映射和结构光解码算法，
/// 但不依赖 CalibScanStateStore / CalibPointCloudStateStore，也不写 Blob 或数据库。
/// </summary>
public sealed class ProductionPointCloudScanService
    : IProductionPointCloudScanService,
        ITransientDependency
{
    private const int TriggerModeSettleDelayMs = 100;
    private const int TextureFrameSettleDelayMs = 80;
    private const int MinimumReliableStructuredLightMatches = 500;
    internal const int MaximumPointCount = 5_000_000;
    internal const long MaximumPointCloudBytes = 512L * 1024 * 1024;

    private readonly IRepository<CalibProject, Guid> _projectRepository;
    private readonly IRepository<CalibCameraParam, Guid> _cameraParamRepository;
    private readonly IRepository<CalibStereoResult, Guid> _stereoResultRepository;
    private readonly IRepository<CalibProjectorParam, Guid> _projectorParamRepository;
    private readonly IRepository<CameraDevice, Guid> _cameraDeviceRepository;
    private readonly IBlobContainer<CalibPhotoBlobContainer> _blobContainer;
    private readonly ICameraDriverRegistry _cameraDrivers;
    private readonly IProjectorConnectionPool _projectorConnectionPool;
    private readonly IDeviceOperationSessionManager _deviceSessions;
    private readonly IAbpDistributedLock _distributedLock;
    private readonly ILogger<ProductionPointCloudScanService> _logger;

    public ProductionPointCloudScanService(
        IRepository<CalibProject, Guid> projectRepository,
        IRepository<CalibCameraParam, Guid> cameraParamRepository,
        IRepository<CalibStereoResult, Guid> stereoResultRepository,
        IRepository<CalibProjectorParam, Guid> projectorParamRepository,
        IRepository<CameraDevice, Guid> cameraDeviceRepository,
        IBlobContainer<CalibPhotoBlobContainer> blobContainer,
        ICameraDriverRegistry cameraDrivers,
        IProjectorConnectionPool projectorConnectionPool,
        IDeviceOperationSessionManager deviceSessions,
        IAbpDistributedLock distributedLock,
        ILogger<ProductionPointCloudScanService> logger
    )
    {
        _projectRepository = projectRepository;
        _cameraParamRepository = cameraParamRepository;
        _stereoResultRepository = stereoResultRepository;
        _projectorParamRepository = projectorParamRepository;
        _cameraDeviceRepository = cameraDeviceRepository;
        _blobContainer = blobContainer;
        _cameraDrivers = cameraDrivers;
        _projectorConnectionPool = projectorConnectionPool;
        _deviceSessions = deviceSessions;
        _distributedLock = distributedLock;
        _logger = logger;
    }

    public async Task<ProductionPointCloudScanResult> ScanAsync(
        ProductionPointCloudScanRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ValidateRequest(request);
        cancellationToken.ThrowIfCancellationRequested();

        using ProductionScanProfile profile = await LoadProfileAsync(
            request.CalibProjectId,
            cancellationToken
        );
        await using ProductionHardwareLease hardwareLease = await AcquireHardwareAsync(
            profile,
            cancellationToken
        );

        Stopwatch timer = Stopwatch.StartNew();
        List<PointCloudData> rounds = new(request.CycleCount);
        TablePlaneModel? tablePlane = null;
        IDlpProjectorService? projector = null;

        try
        {
            await PrepareCamerasAsync(profile, cancellationToken);
            if (profile.Mode == CalibDeviceType.TwoCamera1Light)
            {
                projector = _projectorConnectionPool.TryGet(profile.ProjectorDeviceId!.Value)
                    ?? throw new UserFriendlyException(
                        "[SCAN_PROJECTOR_OFFLINE] 投影仪尚未连接，请先在设备管理页连接。"
                    );
                await PrepareProjectorAsync(projector, cancellationToken);
            }

            for (int cycle = 1; cycle <= request.CycleCount; cycle++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PointCloudData cloud;
                if (profile.Mode == CalibDeviceType.TwoCamera1Light)
                {
                    StructuredLightFrames frames = await CaptureStructuredLightCycleAsync(
                        profile,
                        projector!,
                        cancellationToken
                    );
                    cloud = ReconstructStructuredLight(
                        profile,
                        frames,
                        request,
                        ref tablePlane,
                        cancellationToken
                    );
                }
                else
                {
                    StereoFrames frames = await CapturePassiveStereoCycleAsync(
                        profile,
                        cancellationToken
                    );
                    cloud = ReconstructPassiveStereo(
                        profile,
                        frames,
                        request,
                        ref tablePlane,
                        cancellationToken
                    );
                }

                try
                {
                    EnsurePointCloudLimits(rounds.Append(cloud));
                    rounds.Add(cloud);
                }
                catch
                {
                    cloud.DisposePointCloud();
                    throw;
                }
                _logger.LogInformation(
                    "生产扫描周期完成：ProjectId={ProjectId}, Cycle={Cycle}/{CycleCount}, Points={Points}",
                    request.CalibProjectId,
                    cycle,
                    request.CycleCount,
                    cloud.PointCount
                );
            }

            PointCloudData merged = MergePointClouds(rounds);
            rounds.Clear();
            timer.Stop();
            return new ProductionPointCloudScanResult(
                merged,
                merged.PointCount,
                request.CycleCount,
                timer.ElapsedMilliseconds
            );
        }
        finally
        {
            foreach (PointCloudData round in rounds)
                round.DisposePointCloud();
            await CleanupHardwareAsync(profile, projector);
        }
    }

    private static void ValidateRequest(ProductionPointCloudScanRequest request)
    {
        if (request.CalibProjectId == Guid.Empty)
            throw new ArgumentException("必须选择有效的标定项目。", nameof(request));
        if (request.CycleCount is < 1 or > 20)
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "扫描周期必须在 1 到 20 之间。"
            );
        if (request.TableClearanceMm is < 0 or > 50)
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "台面净空必须在 0 到 50 mm 之间。"
            );
    }

    private async Task<ProductionScanProfile> LoadProfileAsync(
        Guid projectId,
        CancellationToken cancellationToken
    )
    {
        CalibProject project = await _projectRepository.GetAsync(
            projectId,
            cancellationToken: cancellationToken
        );
        if (project.DeviceType == CalibDeviceType.OneCamera1Light)
            throw new UserFriendlyException(
                "[SCAN_MODE_UNSUPPORTED] 单相机结构光尚无可用的投影仪-相机三角测量标定，生产扫描只支持普通双目或双目结构光。"
            );
        if (project.DeviceType is not (
            CalibDeviceType.TwoCamera0Light or CalibDeviceType.TwoCamera1Light
        ))
            throw new UserFriendlyException("[SCAN_MODE_UNSUPPORTED] 当前标定项目不支持生产扫描。");
        if (!project.MainCameraDeviceId.HasValue || !project.SecondaryCameraDeviceId.HasValue)
            throw new UserFriendlyException("[SCAN_BINDING_MISSING] 生产扫描必须绑定主、从两台相机。");
        if (
            project.DeviceType == CalibDeviceType.TwoCamera1Light
            && !project.BoundProjectorDeviceId.HasValue
        )
            throw new UserFriendlyException("[SCAN_BINDING_MISSING] 双目结构光扫描必须绑定投影仪。");

        CalibStereoResult? stereo = await _stereoResultRepository.FirstOrDefaultAsync(
            x => x.CalibProjectId == project.Id,
            cancellationToken: cancellationToken
        );
        if (stereo is null)
            throw new UserFriendlyException("[SCAN_CALIBRATION_MISSING] 尚未生成双目标定结果。");
        if (
            stereo.MainCameraDeviceId != project.MainCameraDeviceId.Value
            || stereo.SecondaryCameraDeviceId != project.SecondaryCameraDeviceId.Value
        )
            throw new UserFriendlyException(
                "[SCAN_CALIBRATION_STALE] 当前主从相机绑定与双目标定结果不一致，请重新标定。"
            );
        if (stereo.StereoReprojectionError > CalibConsts.MaxStereoReprojectionError)
            throw new UserFriendlyException(
                $"[SCAN_CALIBRATION_INVALID] 双目标定误差 {stereo.StereoReprojectionError:F4}px 超限，请重新标定。"
            );

        CameraDevice mainCamera = await _cameraDeviceRepository.GetAsync(
            project.MainCameraDeviceId.Value,
            cancellationToken: cancellationToken
        );
        CameraDevice secondaryCamera = await _cameraDeviceRepository.GetAsync(
            project.SecondaryCameraDeviceId.Value,
            cancellationToken: cancellationToken
        );
        CameraRuntimeBinding mainBinding = ResolveCamera(mainCamera);
        CameraRuntimeBinding secondaryBinding = ResolveCamera(secondaryCamera);

        using Mat p1 = CalibImageUtils.DeserializeMatrix(stereo.ProjectionP1Json, 3, 4);
        using Mat p2 = CalibImageUtils.DeserializeMatrix(stereo.ProjectionP2Json, 3, 4);
        using Mat baseline = StereoReconstructionUtils.ComputeBaselineFromStereoResult(p1, p2);
        double baselineMm = StereoReconstructionUtils.ComputeBaselineDistance(baseline);
        if (!double.IsFinite(baselineMm) || baselineMm <= 0)
            throw new UserFriendlyException("[SCAN_CALIBRATION_INVALID] 双目标定基线无效。");

        (Mat map1x, Mat map1y, Mat map2x, Mat map2y) = await LoadRectificationMapsAsync(
            stereo,
            cancellationToken
        );
        try
        {
            CalibProjectorParam? projectorParam = null;
            if (project.DeviceType == CalibDeviceType.TwoCamera1Light)
            {
                projectorParam = await _projectorParamRepository.FirstOrDefaultAsync(
                    x => x.CalibProjectId == project.Id,
                    cancellationToken: cancellationToken
                );
                ValidateProjectorParameters(project, projectorParam);
            }

            return new ProductionScanProfile(
                project.Id,
                project.DeviceType,
                mainBinding,
                secondaryBinding,
                project.BoundProjectorDeviceId,
                p1.Clone(),
                p2.Clone(),
                baselineMm,
                map1x,
                map1y,
                map2x,
                map2y,
                projectorParam?.PatternCount ?? 0,
                projectorParam?.PeriodCount ?? 0,
                projectorParam?.ResolutionWidth ?? 0,
                projectorParam?.ResolutionHeight ?? 0,
                string.Equals(
                    projectorParam?.FringeType,
                    "wb",
                    StringComparison.OrdinalIgnoreCase
                )
            );
        }
        catch
        {
            map1x.Dispose();
            map1y.Dispose();
            map2x.Dispose();
            map2y.Dispose();
            throw;
        }
    }

    private static void ValidateProjectorParameters(
        CalibProject project,
        CalibProjectorParam? parameter
    )
    {
        if (parameter is null)
            throw new UserFriendlyException("[SCAN_CALIBRATION_MISSING] 请先完成投影仪条纹参数配置。");
        if (parameter.ProjectorDeviceId != project.BoundProjectorDeviceId)
            throw new UserFriendlyException(
                "[SCAN_CALIBRATION_STALE] 条纹参数与当前绑定投影仪不一致，请重新保存。"
            );
        if (
            parameter.PatternCount < 3
            || parameter.PeriodCount <= 0
            || parameter.ResolutionWidth <= 0
            || parameter.ResolutionHeight <= 0
            || parameter.ResolutionWidth % parameter.PeriodCount != 0
            || parameter.ResolutionHeight % parameter.PeriodCount != 0
        )
            throw new UserFriendlyException("[SCAN_CALIBRATION_INVALID] 投影仪条纹参数无效。");
    }

    private async Task<(Mat Map1x, Mat Map1y, Mat Map2x, Mat Map2y)>
        LoadRectificationMapsAsync(
            CalibStereoResult stereo,
            CancellationToken cancellationToken
        )
    {
        if (stereo.RectifyMapWidth <= 0 || stereo.RectifyMapHeight <= 0)
            throw new UserFriendlyException("[SCAN_CALIBRATION_INVALID] 双目矫正映射尺寸无效。");

        string[] keys =
        [
            stereo.Map1XBlobKey,
            stereo.Map1YBlobKey,
            stereo.Map2XBlobKey,
            stereo.Map2YBlobKey,
        ];
        foreach (string key in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(key) || !await _blobContainer.ExistsAsync(key))
                throw new UserFriendlyException(
                    "[SCAN_CALIBRATION_MISSING] 双目矫正映射缺失，请重新执行双目标定。"
                );
        }

        int byteCount = checked(
            stereo.RectifyMapWidth * stereo.RectifyMapHeight * sizeof(float)
        );
        byte[][] bytes = new byte[keys.Length][];
        for (int i = 0; i < keys.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bytes[i] = await _blobContainer.GetAllBytesAsync(keys[i]);
            if (bytes[i].Length != byteCount)
                throw new UserFriendlyException(
                    "[SCAN_CALIBRATION_INVALID] 双目矫正映射尺寸不匹配，请重新标定。"
                );
        }

        Mat[] maps =
        [
            new(stereo.RectifyMapHeight, stereo.RectifyMapWidth, MatType.CV_32FC1),
            new(stereo.RectifyMapHeight, stereo.RectifyMapWidth, MatType.CV_32FC1),
            new(stereo.RectifyMapHeight, stereo.RectifyMapWidth, MatType.CV_32FC1),
            new(stereo.RectifyMapHeight, stereo.RectifyMapWidth, MatType.CV_32FC1),
        ];
        try
        {
            for (int i = 0; i < maps.Length; i++)
                Marshal.Copy(bytes[i], 0, maps[i].Data, byteCount);
            return (maps[0], maps[1], maps[2], maps[3]);
        }
        catch
        {
            foreach (Mat map in maps)
                map.Dispose();
            throw;
        }
    }

    private CameraRuntimeBinding ResolveCamera(CameraDevice camera)
    {
        if (string.IsNullOrWhiteSpace(camera.HardwareId))
            throw new UserFriendlyException(
                $"[SCAN_CAMERA_OFFLINE] 相机 [{camera.Name}] 尚未绑定稳定硬件标识。"
            );
        ICameraDriver driver = _cameraDrivers.GetRequired(camera.DriverId);
        if (driver is not ITucamCameraService tucam)
            throw new UserFriendlyException(
                $"[SCAN_CAMERA_UNSUPPORTED] 相机驱动 [{camera.DriverId}] 不支持生产扫描。"
            );
        if (!driver.TryGetRuntimeIndex(camera.HardwareId, out int runtimeIndex))
            throw new UserFriendlyException($"[SCAN_CAMERA_OFFLINE] 相机 [{camera.Name}] 当前离线。");
        return new CameraRuntimeBinding(camera, tucam, runtimeIndex);
    }

    private async Task<ProductionHardwareLease> AcquireHardwareAsync(
        ProductionScanProfile profile,
        CancellationToken cancellationToken
    )
    {
        List<(Guid Id, DeviceType Type)> devices =
        [
            (profile.Main.Camera.Id, DeviceType.Camera),
            (profile.Secondary.Camera.Id, DeviceType.Camera),
        ];
        if (profile.ProjectorDeviceId.HasValue)
            devices.Add((profile.ProjectorDeviceId.Value, DeviceType.Projector));
        devices = devices
            .Distinct()
            .OrderBy(x => x.Id)
            .ThenBy(x => x.Type)
            .ToList();

        string sessionId = $"workflow-scan:{Guid.NewGuid():N}";
        List<IAbpDistributedLockHandle> distributedHandles = [];
        List<Guid> softLeases = [];
        try
        {
            foreach ((Guid id, DeviceType type) in devices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IAbpDistributedLockHandle? handle = await _distributedLock.TryAcquireAsync(
                    $"production-scan:{type}:{id:N}",
                    TimeSpan.Zero
                );
                if (handle is null)
                    throw new UserFriendlyException(
                        $"[SCAN_DEVICE_BUSY] {type} 设备 {id:D} 正被其他扫描任务占用。"
                    );
                distributedHandles.Add(handle);
            }

            foreach ((Guid id, DeviceType type) in devices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _deviceSessions.TryAcquire(
                    id,
                    type,
                    sessionId,
                    userId: null,
                    userName: "工作流生产扫描",
                    force: false,
                    neverExpire: true
                );
                softLeases.Add(id);
            }

            return new ProductionHardwareLease(
                _deviceSessions,
                sessionId,
                softLeases,
                distributedHandles
            );
        }
        catch
        {
            foreach (Guid id in softLeases.AsEnumerable().Reverse())
                _deviceSessions.Release(id, sessionId);
            foreach (IAbpDistributedLockHandle handle in distributedHandles.AsEnumerable().Reverse())
                await handle.DisposeAsync();
            throw;
        }
    }

    private static async Task PrepareCamerasAsync(
        ProductionScanProfile profile,
        CancellationToken cancellationToken
    )
    {
        foreach (CameraRuntimeBinding binding in new[] { profile.Main, profile.Secondary })
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!binding.Service.IsCameraOpen(binding.RuntimeIndex))
                throw new UserFriendlyException(
                    $"[SCAN_CAMERA_OFFLINE] 相机 [{binding.Camera.Name}] 未打开。"
                );
            binding.OriginalTriggerSource = await binding.Service.GetGenICamIntAsync(
                binding.RuntimeIndex,
                "TriggerSource"
            );
            binding.OriginalTriggerMode = await binding.Service.GetGenICamIntAsync(
                binding.RuntimeIndex,
                "TriggerMode"
            );
            if (profile.Mode == CalibDeviceType.TwoCamera1Light)
            {
                await SetAndVerifyTriggerModeAsync(binding, expectedMode: 1);
            }
            else
            {
                // 无投影仪的普通双目保留软件触发短会话。
                await binding.Service.SetGenICamIntAsync(binding.RuntimeIndex, "TriggerSource", 1);
                await SetAndVerifyTriggerModeAsync(binding, expectedMode: 2);
            }
        }
    }

    private static async Task PrepareProjectorAsync(
        IDlpProjectorService projector,
        CancellationToken cancellationToken
    )
    {
        await RequireProjectorAsync(
            projector.LedOnAsync(cancellationToken),
            "LedOn"
        );
        await RequireProjectorAsync(
            projector.SetBootImageAsync(ProjectorBootImage.Cross, cancellationToken),
            "SetBootImage(Cross)"
        );
        await RequireProjectorAsync(
            projector.SetDisplayModeAsync(ProjectorDisplayMode.Cross, cancellationToken),
            "SetDisplayMode(Cross)"
        );
    }

    private async Task<StructuredLightFrames> CaptureStructuredLightCycleAsync(
        ProductionScanProfile profile,
        IDlpProjectorService projector,
        CancellationToken cancellationToken
    )
    {
        int frameCount = GrayPhasePatternLayout.GetTotalFrameCount(
            profile.PeriodCount,
            profile.PatternCount
        );
        List<byte[]> main = new(frameCount);
        List<byte[]> secondary = new(frameCount);

        await projector.ConfigureFringePlaybackAsync(
            frameCount,
            GrayPhasePatternLayout.GetFramesPerDirection(
                profile.PeriodCount,
                profile.PatternCount
            ),
            cancellationToken
        );

        await RequireProjectorAsync(
            projector.SetTriggerModeAsync(ProjectorTriggerMode.Normal, cancellationToken),
            "SetTriggerMode(Normal)"
        );
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        await RequireProjectorAsync(
            projector.SetTriggerModeAsync(ProjectorTriggerMode.SingleFrame, cancellationToken),
            "SetTriggerMode(SingleFrame)"
        );
        await Task.Delay(TriggerModeSettleDelayMs, cancellationToken);

        // 上一周期的白光纹理帧会把主相机切到 FreeRunning；每周期重新确认两台
        // 相机均为 Standard 外触发，再同时保持 Cap_Start(TriggerStandard)。
        await SetAndVerifyTriggerModeAsync(profile.Main, expectedMode: 1);
        await SetAndVerifyTriggerModeAsync(profile.Secondary, expectedMode: 1);
        await using TucamHardwareTriggerCaptureSession triggerSession =
            await CreateHardwareTriggerSessionAsync(profile, cancellationToken);

        for (int frame = 0; frame < frameCount; frame++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool firstFrame = frame == 0;
            IReadOnlyList<byte[]> pair = await triggerSession.CaptureAsync(
                ct =>
                    RequireProjectorAsync(
                        firstFrame
                            ? projector.TriggerOnceAsync(ct)
                            : projector.NextFrameAsync(ct),
                        firstFrame ? "TriggerOnce" : "NextFrame"
                    ),
                cancellationToken
            );
            main.Add(pair[0]);
            secondary.Add(pair[1]);
        }

        // 必须先停止硬触发会话，才能将主相机切回自由运行拍摄白光纹理。
        await triggerSession.DisposeAsync();

        await RequireProjectorAsync(
            projector.SetDisplayModeAsync(ProjectorDisplayMode.White, cancellationToken),
            "SetDisplayMode(White)"
        );
        await SetAndVerifyTriggerModeAsync(profile.Main, expectedMode: 0);
        await Task.Delay(TextureFrameSettleDelayMs, cancellationToken);
        byte[] texture = await GrabFrameAsync(profile.Main, cancellationToken);
        return new StructuredLightFrames(main, secondary, texture);
    }

    private static async Task<TucamHardwareTriggerCaptureSession>
        CreateHardwareTriggerSessionAsync(
            ProductionScanProfile profile,
            CancellationToken cancellationToken
        )
    {
        TucamTriggeredCamera[] cameras =
        [
            await CreateTriggeredCameraAsync(profile.Main, cancellationToken),
            await CreateTriggeredCameraAsync(profile.Secondary, cancellationToken),
        ];
        return await TucamHardwareTriggerCaptureSession.StartAsync(cameras, cancellationToken);
    }

    private static async Task<TucamTriggeredCamera> CreateTriggeredCameraAsync(
        CameraRuntimeBinding binding,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        long exposureUs = await binding.Service.GetGenICamIntAsync(
            binding.RuntimeIndex,
            "ExposureTime"
        );
        int timeoutMs = (int)Math.Clamp(exposureUs / 1000L * 2 + 1000L, 8000L, 30000L);
        return new TucamTriggeredCamera(
            binding.Service,
            binding.RuntimeIndex,
            timeoutMs,
            binding.Camera.ImageRotationAngle
        );
    }

    private static async Task SetAndVerifyTriggerModeAsync(
        CameraRuntimeBinding binding,
        long expectedMode
    )
    {
        if (binding.Service.IsCapturing(binding.RuntimeIndex))
            await binding.Service.StopCaptureAsync(binding.RuntimeIndex);
        await binding.Service.SetGenICamIntAsync(
            binding.RuntimeIndex,
            "TriggerMode",
            expectedMode
        );
        long actualMode = await binding.Service.GetGenICamIntAsync(
            binding.RuntimeIndex,
            "TriggerMode"
        );
        if (actualMode != expectedMode)
            throw new InvalidOperationException(
                $"[SCAN_TRIGGER_MODE_FAILED] 相机 {binding.Camera.Name} TriggerMode "
                    + $"写入 {expectedMode} 后读回 {actualMode}。"
            );
    }

    private async Task<StereoFrames> CapturePassiveStereoCycleAsync(
        ProductionScanProfile profile,
        CancellationToken cancellationToken
    )
    {
        byte[] main = await GrabFrameAsync(profile.Main, cancellationToken);
        byte[] secondary = await GrabFrameAsync(profile.Secondary, cancellationToken);
        return new StereoFrames(main, secondary);
    }

    private static async Task<byte[]> GrabFrameAsync(
        CameraRuntimeBinding binding,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        await binding.Service.StartCaptureAsync(binding.RuntimeIndex);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            long triggerMode = await binding.Service.GetGenICamIntAsync(
                binding.RuntimeIndex,
                "TriggerMode"
            );
            if (triggerMode == 2)
                await binding.Service.DoSoftwareTriggerAsync(binding.RuntimeIndex);

            long exposureUs = await binding.Service.GetGenICamIntAsync(
                binding.RuntimeIndex,
                "ExposureTime"
            );
            int timeoutMs = (int)Math.Clamp(exposureUs / 1000L * 2 + 1000L, 8000L, 30000L);
            return await binding.Service.GrabFrameRawAsync(
                binding.RuntimeIndex,
                timeoutMs,
                maxWidth: 0,
                jpegQuality: 85,
                imageRotationAngle: binding.Camera.ImageRotationAngle,
                cancellationToken: cancellationToken
            );
        }
        finally
        {
            await binding.Service.StopCaptureAsync(binding.RuntimeIndex);
        }
    }

    private PointCloudData ReconstructStructuredLight(
        ProductionScanProfile profile,
        StructuredLightFrames frames,
        ProductionPointCloudScanRequest request,
        ref TablePlaneModel? cachedTablePlane,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        GrayPhaseDecodeResult mainDecode = GrayPhaseStructuredLightDecoder.Decode(
            frames.Main,
            profile.PeriodCount,
            profile.PatternCount,
            profile.ProjectorWidth,
            profile.ProjectorHeight,
            profile.InvertedPatterns,
            profile.Map1x,
            profile.Map1y,
            cancellationToken
        );
        GrayPhaseDecodeResult secondaryDecode = GrayPhaseStructuredLightDecoder.Decode(
            frames.Secondary,
            profile.PeriodCount,
            profile.PatternCount,
            profile.ProjectorWidth,
            profile.ProjectorHeight,
            profile.InvertedPatterns,
            profile.Map2x,
            profile.Map2y,
            cancellationToken
        );
        int disparitySign = StereoReconstructionUtils.ComputeDisparitySign(
            profile.ProjectionP1,
            profile.ProjectionP2
        );
        using Mat disparity = GrayPhaseStructuredLightDecoder.Match(
            mainDecode,
            secondaryDecode,
            disparitySign,
            profile.ProjectionP1.At<double>(0, 2)
                - profile.ProjectionP2.At<double>(0, 2),
            out double[] secondaryYCoordinates,
            out GrayPhaseMatchDiagnostics diagnostics
        );
        _logger.LogInformation(
            "Structured-light match filtering: ProjectId={ProjectId}, Matched={Matched}, "
                + "BidirectionalRejected={BidirectionalRejected}, "
                + "VerticalStripeRejected={VerticalStripeRejected}",
            profile.ProjectId,
            diagnostics.MatchedPixels,
            diagnostics.BidirectionalRejected,
            diagnostics.VerticalStripeRejected
        );
        if (diagnostics.MatchedPixels < MinimumReliableStructuredLightMatches)
            throw new InvalidOperationException(
                $"[SCAN_MATCH_TOO_FEW] 结构光有效匹配仅 {diagnostics.MatchedPixels}/"
                    + $"{MinimumReliableStructuredLightMatches}，请检查曝光、对焦、条纹顺序和同步。"
            );

        cancellationToken.ThrowIfCancellationRequested();
        using Mat depth = StereoReconstructionUtils.ComputeDepthFromDisparity(
            disparity,
            profile.ProjectionP1,
            profile.ProjectionP2,
            profile.BaselineMm,
            disparitySign,
            secondaryYCoordinates
        );
        ApplyTableFilter(depth, profile, request, ref cachedTablePlane);
        RemoveSparseFarDepthOutliers(depth, profile.ProjectionP1, profile.ProjectId);
        using Mat texture = CalibImageUtils.LoadBgrMat(frames.Texture);
        using Mat rectifiedTexture = new();
        Cv2.Remap(
            texture,
            rectifiedTexture,
            profile.Map1x,
            profile.Map1y,
            InterpolationFlags.Linear
        );
        cancellationToken.ThrowIfCancellationRequested();
        (Mat points, Mat colors) = StereoReconstructionUtils.GeneratePointCloud(
            depth,
            rectifiedTexture,
            profile.ProjectionP1,
            sampleStep: 1
        );
        return OwnPointCloud(points, colors, "结构光重建未生成有效点云。");
    }

    private PointCloudData ReconstructPassiveStereo(
        ProductionScanProfile profile,
        StereoFrames frames,
        ProductionPointCloudScanRequest request,
        ref TablePlaneModel? cachedTablePlane,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        using Mat mainImage = CalibImageUtils.LoadBgrMat(frames.Main);
        using Mat secondaryImage = CalibImageUtils.LoadBgrMat(frames.Secondary);
        (Mat rectifiedMain, Mat rectifiedSecondary) = StereoReconstructionUtils.RectifyImages(
            mainImage,
            secondaryImage,
            profile.Map1x,
            profile.Map1y,
            profile.Map2x,
            profile.Map2y
        );
        using (rectifiedMain)
        using (rectifiedSecondary)
        using (Mat mainGray = new())
        using (Mat secondaryGray = new())
        using (Mat disparity16 = new())
        using (Mat disparity = new())
        {
            Cv2.CvtColor(rectifiedMain, mainGray, ColorConversionCodes.BGR2GRAY);
            Cv2.CvtColor(rectifiedSecondary, secondaryGray, ColorConversionCodes.BGR2GRAY);
            int disparitySign = StereoReconstructionUtils.ComputeDisparitySign(
                profile.ProjectionP1,
                profile.ProjectionP2
            );
            int minimumDisparity = disparitySign > 0 ? 0 : -128;
            using StereoBM matcher = StereoBM.Create(numDisparities: 128, blockSize: 15);
            matcher.MinDisparity = minimumDisparity;
            matcher.Compute(mainGray, secondaryGray, disparity16);
            disparity16.ConvertTo(disparity, MatType.CV_64FC1, 1d / 16d);
            cancellationToken.ThrowIfCancellationRequested();

            using Mat depth = StereoReconstructionUtils.ComputeDepthFromDisparity(
                disparity,
                profile.ProjectionP1,
                profile.ProjectionP2,
                profile.BaselineMm,
                disparitySign
            );
            ApplyTableFilter(depth, profile, request, ref cachedTablePlane);
            RemoveSparseFarDepthOutliers(depth, profile.ProjectionP1, profile.ProjectId);
            (Mat points, Mat colors) = StereoReconstructionUtils.GeneratePointCloud(
                depth,
                rectifiedMain,
                profile.ProjectionP1,
                sampleStep: 8
            );
            return OwnPointCloud(points, colors, "普通双目重建未生成有效点云。");
        }
    }

    private void ApplyTableFilter(
        Mat depth,
        ProductionScanProfile profile,
        ProductionPointCloudScanRequest request,
        ref TablePlaneModel? cachedTablePlane
    )
    {
        if (!request.EnableTableFilter)
            return;
        TablePlaneFilterResult filter = TablePlaneFilter.Apply(
            depth,
            profile.ProjectionP1,
            request.TableClearanceMm,
            cachedTablePlane
        );
        if (!filter.Applied || filter.Plane is null)
        {
            _logger.LogWarning(
                "生产扫描未应用台面过滤：ProjectId={ProjectId}, Reason={Reason}, Candidates={Candidates}, Inliers={Inliers}",
                profile.ProjectId,
                filter.FailureReason,
                filter.CandidateCount,
                filter.InlierCount
            );
            return;
        }

        cachedTablePlane ??= filter.Plane;
        _logger.LogInformation(
            "生产扫描台面过滤：ProjectId={ProjectId}, Removed={Removed}, Clearance={Clearance}mm",
            profile.ProjectId,
            filter.RemovedPointCount,
            request.TableClearanceMm
        );
    }

    private void RemoveSparseFarDepthOutliers(
        Mat depth,
        Mat projectionP1,
        Guid projectId)
    {
        DepthOutlierFilterResult result =
            StereoReconstructionUtils.RemoveSparseFarDepthOutliers(depth, projectionP1);
        if (!result.Applied)
            return;

        _logger.LogInformation(
            "Production scan sparse far-depth outliers removed: ProjectId={ProjectId}, "
                + "Removed={Removed}/{Valid}, DepthCutoffMm={DepthCutoffMm:F2}",
            projectId,
            result.RemovedPointCount,
            result.ValidPointCount,
            result.DepthCutoffMm
        );
    }

    private static PointCloudData OwnPointCloud(Mat points, Mat colors, string emptyMessage)
    {
        if (points.Rows == 0)
        {
            points.Dispose();
            colors.Dispose();
            throw new InvalidOperationException($"[SCAN_EMPTY_POINT_CLOUD] {emptyMessage}");
        }

        try
        {
            PointCloudData cloud = new() { Value = points };
            cloud.SetColors(colors);
            return cloud;
        }
        catch
        {
            points.Dispose();
            colors.Dispose();
            throw;
        }
    }

    internal static PointCloudData MergePointClouds(
        IReadOnlyList<PointCloudData> clouds,
        int maximumPointCount = MaximumPointCount,
        long maximumBytes = MaximumPointCloudBytes
    )
    {
        if (clouds.Count == 0)
            throw new InvalidOperationException("[SCAN_EMPTY_POINT_CLOUD] 扫描未产生任何点云。");
        try
        {
            EnsurePointCloudLimits(clouds, maximumPointCount, maximumBytes);
        }
        catch
        {
            foreach (PointCloudData cloud in clouds)
                cloud.DisposePointCloud();
            throw;
        }
        if (clouds.Count == 1)
            return clouds[0];

        int totalRows = checked(clouds.Sum(x => x.PointCount));
        Mat mergedPoints = new(totalRows, 3, MatType.CV_32FC1);
        bool hasColors = clouds.All(x => x.HasColors);
        Mat? mergedColors = hasColors ? new Mat(totalRows, 3, MatType.CV_8UC1) : null;
        try
        {
            int row = 0;
            foreach (PointCloudData cloud in clouds)
            {
                Mat source = cloud.PointCloud
                    ?? throw new InvalidOperationException("扫描周期点云已释放。");
                using (Mat target = mergedPoints.RowRange(row, row + source.Rows))
                    source.CopyTo(target);
                if (hasColors)
                {
                    using Mat colorTarget = mergedColors!.RowRange(row, row + source.Rows);
                    cloud.Colors!.CopyTo(colorTarget);
                }
                row += source.Rows;
            }

            PointCloudData merged = new() { Value = mergedPoints };
            if (mergedColors is not null)
                merged.SetColors(mergedColors);
            foreach (PointCloudData cloud in clouds)
                cloud.DisposePointCloud();
            return merged;
        }
        catch
        {
            mergedPoints.Dispose();
            mergedColors?.Dispose();
            throw;
        }
    }

    private static void EnsurePointCloudLimits(
        IEnumerable<PointCloudData> clouds,
        int maximumPointCount = MaximumPointCount,
        long maximumBytes = MaximumPointCloudBytes
    )
    {
        long points = 0;
        long bytes = 0;
        foreach (PointCloudData cloud in clouds)
        {
            Mat pointMat = cloud.PointCloud
                ?? throw new InvalidOperationException("扫描周期点云已释放。");
            points = checked(points + cloud.PointCount);
            bytes = checked(bytes + EstimateBytes(pointMat));
            if (cloud.Colors is { } colors)
                bytes = checked(bytes + EstimateBytes(colors));
            if (points > maximumPointCount || bytes > maximumBytes)
                throw new InvalidOperationException(
                    $"[SCAN_POINT_CLOUD_LIMIT] 扫描点云累计 {points:N0} 点 / "
                        + $"{bytes / 1024d / 1024d:F1} MiB，超过上限 "
                        + $"{maximumPointCount:N0} 点 / {maximumBytes / 1024d / 1024d:F0} MiB。"
                );
        }
    }

    private static long EstimateBytes(Mat mat) => checked(
        (long)mat.Rows * mat.Cols * mat.ElemSize()
    );

    private async Task CleanupHardwareAsync(
        ProductionScanProfile profile,
        IDlpProjectorService? projector
    )
    {
        foreach (CameraRuntimeBinding binding in new[] { profile.Main, profile.Secondary })
        {
            try
            {
                if (binding.Service.IsCameraOpen(binding.RuntimeIndex))
                    await binding.Service.StopCaptureAsync(binding.RuntimeIndex);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "生产扫描停止相机失败：Camera={Camera}", binding.Camera.Id);
            }

            try
            {
                if (
                    binding.OriginalTriggerSource.HasValue
                    || binding.OriginalTriggerMode.HasValue
                )
                    await binding.Service.SetGenICamIntAsync(
                        binding.RuntimeIndex,
                        "TriggerMode",
                        0
                    );
                if (binding.OriginalTriggerSource.HasValue)
                    await binding.Service.SetGenICamIntAsync(
                        binding.RuntimeIndex,
                        "TriggerSource",
                        binding.OriginalTriggerSource.Value
                    );
                if (binding.OriginalTriggerMode.HasValue)
                    await binding.Service.SetGenICamIntAsync(
                        binding.RuntimeIndex,
                        "TriggerMode",
                        binding.OriginalTriggerMode.Value
                    );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "生产扫描恢复相机触发配置失败：Camera={Camera}",
                    binding.Camera.Id
                );
            }
        }

        if (projector is null)
            return;
        try
        {
            await projector.SetTriggerModeAsync(ProjectorTriggerMode.Normal);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "生产扫描恢复投影仪触发模式失败");
        }
        try
        {
            await projector.LedOffAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "生产扫描关闭投影仪失败");
        }
    }

    private static async Task RequireProjectorAsync(Task<bool> operation, string operationName)
    {
        if (!await operation)
            throw new InvalidOperationException(
                $"[SCAN_PROJECTOR_COMMAND_FAILED] 投影仪命令 {operationName} 执行失败。"
            );
    }

    private sealed class CameraRuntimeBinding(
        CameraDevice camera,
        ITucamCameraService service,
        int runtimeIndex
    )
    {
        public CameraDevice Camera { get; } = camera;
        public ITucamCameraService Service { get; } = service;
        public int RuntimeIndex { get; } = runtimeIndex;
        public long? OriginalTriggerSource { get; set; }
        public long? OriginalTriggerMode { get; set; }
    }

    private sealed record StructuredLightFrames(
        IReadOnlyList<byte[]> Main,
        IReadOnlyList<byte[]> Secondary,
        byte[] Texture
    );

    private sealed record StereoFrames(byte[] Main, byte[] Secondary);

    private sealed class ProductionScanProfile : IDisposable
    {
        public Guid ProjectId { get; }
        public CalibDeviceType Mode { get; }
        public CameraRuntimeBinding Main { get; }
        public CameraRuntimeBinding Secondary { get; }
        public Guid? ProjectorDeviceId { get; }
        public Mat ProjectionP1 { get; }
        public Mat ProjectionP2 { get; }
        public double BaselineMm { get; }
        public Mat Map1x { get; }
        public Mat Map1y { get; }
        public Mat Map2x { get; }
        public Mat Map2y { get; }
        public int PatternCount { get; }
        public int PeriodCount { get; }
        public int ProjectorWidth { get; }
        public int ProjectorHeight { get; }
        public bool InvertedPatterns { get; }

        public ProductionScanProfile(
            Guid projectId,
            CalibDeviceType mode,
            CameraRuntimeBinding main,
            CameraRuntimeBinding secondary,
            Guid? projectorDeviceId,
            Mat projectionP1,
            Mat projectionP2,
            double baselineMm,
            Mat map1x,
            Mat map1y,
            Mat map2x,
            Mat map2y,
            int patternCount,
            int periodCount,
            int projectorWidth,
            int projectorHeight,
            bool invertedPatterns
        )
        {
            ProjectId = projectId;
            Mode = mode;
            Main = main;
            Secondary = secondary;
            ProjectorDeviceId = projectorDeviceId;
            ProjectionP1 = projectionP1;
            ProjectionP2 = projectionP2;
            BaselineMm = baselineMm;
            Map1x = map1x;
            Map1y = map1y;
            Map2x = map2x;
            Map2y = map2y;
            PatternCount = patternCount;
            PeriodCount = periodCount;
            ProjectorWidth = projectorWidth;
            ProjectorHeight = projectorHeight;
            InvertedPatterns = invertedPatterns;
        }

        public void Dispose()
        {
            ProjectionP1.Dispose();
            ProjectionP2.Dispose();
            Map1x.Dispose();
            Map1y.Dispose();
            Map2x.Dispose();
            Map2y.Dispose();
        }
    }

    private sealed class ProductionHardwareLease : IAsyncDisposable
    {
        private readonly IDeviceOperationSessionManager _sessions;
        private readonly string _sessionId;
        private readonly IReadOnlyList<Guid> _deviceIds;
        private readonly IReadOnlyList<IAbpDistributedLockHandle> _distributedHandles;
        private int _disposed;

        public ProductionHardwareLease(
            IDeviceOperationSessionManager sessions,
            string sessionId,
            IReadOnlyList<Guid> deviceIds,
            IReadOnlyList<IAbpDistributedLockHandle> distributedHandles
        )
        {
            _sessions = sessions;
            _sessionId = sessionId;
            _deviceIds = deviceIds;
            _distributedHandles = distributedHandles;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            foreach (Guid id in _deviceIds.Reverse())
                _sessions.Release(id, _sessionId);
            foreach (IAbpDistributedLockHandle handle in _distributedHandles.Reverse())
                await handle.DisposeAsync();
        }
    }
}
