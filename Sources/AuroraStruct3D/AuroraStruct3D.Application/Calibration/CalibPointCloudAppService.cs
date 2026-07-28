using AuroraStruct3D.Calibration.Dtos;
using AuroraStruct3D.Cameras;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System.Text;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace AuroraStruct3D.Calibration;

[Authorize]
public class CalibPointCloudAppService : AuroraStruct3DAppService, ICalibPointCloudAppService
{
    // 当前设备基线约 164mm、焦距约 3026px，近距离扫描视差会显著超过 127px。
    // 1024px 对应约 0.48m 的最小深度，覆盖当前结构光工作距离。
    private const double MaxStructuredLightDisparity = 1024d;
    private const int MinimumReliableStructuredLightMatches = 500;

    private readonly IRepository<CalibProject, Guid> _projectRepository;
    private readonly IRepository<CalibCameraParam, Guid> _cameraParamRepository;
    private readonly IRepository<CalibStereoResult, Guid> _stereoResultRepository;
    private readonly IRepository<CalibProjectorParam, Guid> _projectorParamRepository;
    private readonly IRepository<CameraDevice, Guid> _cameraDeviceRepository;
    private readonly CalibPointCloudStateStore _stateStore;
    private readonly ICalibPointCloudNotifier _notifier;
    private readonly IBlobContainer<CalibPhotoBlobContainer> _blobContainer;
    private readonly ILogger<CalibPointCloudAppService> _logger;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public CalibPointCloudAppService(
        IRepository<CalibProject, Guid> projectRepository,
        IRepository<CalibCameraParam, Guid> cameraParamRepository,
        IRepository<CalibStereoResult, Guid> stereoResultRepository,
        IRepository<CalibProjectorParam, Guid> projectorParamRepository,
        IRepository<CameraDevice, Guid> cameraDeviceRepository,
        CalibPointCloudStateStore stateStore,
        ICalibPointCloudNotifier notifier,
        IBlobContainer<CalibPhotoBlobContainer> blobContainer,
        ILogger<CalibPointCloudAppService> logger,
        IUnitOfWorkManager unitOfWorkManager
    )
    {
        _projectRepository = projectRepository;
        _cameraParamRepository = cameraParamRepository;
        _stereoResultRepository = stereoResultRepository;
        _projectorParamRepository = projectorParamRepository;
        _cameraDeviceRepository = cameraDeviceRepository;
        _stateStore = stateStore;
        _notifier = notifier;
        _blobContainer = blobContainer;
        _logger = logger;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public async Task<PointCloudStatusDto> GenerateAsync(GeneratePointCloudInput input)
    {
        CalibProject project = await _projectRepository.GetAsync(input.CalibProjectId);

        PointCloudSessionState session = _stateStore.GetOrCreate(project.Id);
        if (session.IsRunning)
        {
            throw new UserFriendlyException("点云生成任务正在进行中，请等待完成或先取消。");
        }

        (PointCloudSessionState started, CancellationToken token) = _stateStore.Start(project.Id);
        PointCloudStatusDto startDto = started.ToDto();

        _logger.LogInformation("Step7 点云生成启动：ProjectId={ProjectId}", project.Id);

        await _notifier.NotifyStatusAsync(startDto);

        _ = Task.Run(async () => await RunGenerationAsync(project, token), token);

        return startDto;
    }

    public async Task<PointCloudStatusDto> CancelAsync(Guid calibProjectId)
    {
        bool canceled = _stateStore.Cancel(calibProjectId);

        PointCloudSessionState session = _stateStore.GetOrCreate(calibProjectId);
        PointCloudStatusDto dto = session.ToDto();

        if (canceled)
        {
            _logger.LogInformation("Step7 点云生成已取消：ProjectId={ProjectId}", calibProjectId);
            await _notifier.NotifyStatusAsync(dto);
        }

        return dto;
    }

    public async Task<PointCloudStatusDto> GetStatusAsync(Guid calibProjectId)
    {
        await _projectRepository.GetAsync(calibProjectId);
        PointCloudSessionState session = _stateStore.GetOrCreate(calibProjectId);
        return session.ToDto();
    }

    public async Task<byte[]> DownloadPlyAsync(Guid calibProjectId)
    {
        PointCloudSessionState session = _stateStore.GetOrCreate(calibProjectId);
        if (session.State != PointCloudRunState.Completed || string.IsNullOrEmpty(session.PlyBlobKey))
        {
            throw new UserFriendlyException("点云文件不存在或尚未生成完成");
        }

        return await _blobContainer.GetAllBytesAsync(session.PlyBlobKey);
    }

    public async Task GenerateIncrementalPointCloudAsync(
        Guid calibProjectId,
        long roundIndex,
        int totalFrameCount)
    {
        using IUnitOfWork unitOfWork = _unitOfWorkManager.Begin(
            requiresNew: true,
            isTransactional: false
        );
        try
        {
            CalibProject project = await _projectRepository.GetAsync(calibProjectId);

            if (!_stateStore.IsIncrementalModeActive(calibProjectId))
            {
                _stateStore.StartIncrementalMode(calibProjectId);
            }

            var calibrationData = await LoadCalibrationDataAsync(project, CancellationToken.None);
            if (calibrationData == null)
            {
                _logger.LogWarning("Step7 增量点云生成：标定数据不完整，跳过本轮 {Round}", roundIndex);
                return;
            }

            int expectedFrameCount = GrayCodePatternLayout.TotalFrameCount;
            if (totalFrameCount != expectedFrameCount)
            {
                throw new InvalidOperationException(
                    $"结构光条纹帧数必须为 {expectedFrameCount}，实际为 {totalFrameCount}"
                );
            }

            var scanImages = await LoadScanImagesForRoundAsync(
                project,
                roundIndex,
                expectedFrameCount
            );
            if (
                scanImages.MainImages.Count != expectedFrameCount
                || scanImages.SecondaryImages.Count != expectedFrameCount
            )
            {
                throw new InvalidOperationException(
                    $"第 {roundIndex} 轮扫描图像不完整："
                    + $"主相机 {scanImages.MainImages.Count}/{expectedFrameCount}，"
                    + $"从相机 {scanImages.SecondaryImages.Count}/{expectedFrameCount}"
                );
            }

            if (scanImages.TextureImage is null)
            {
                throw new InvalidOperationException(
                    $"第 {roundIndex} 轮缺少主相机白光纹理帧，无法进行真实色彩还原"
                );
            }

            PointCloudStatusDto? reconstructing = _stateStore.UpdateProgress(
                calibProjectId,
                50,
                $"正在重建第 {roundIndex} 轮彩色点云"
            );
            if (reconstructing is not null)
            {
                await _notifier.NotifyStatusAsync(reconstructing);
            }

            GrayCodeDecodeResult mainCode = DecodeGrayCodeForCamera(
                scanImages.MainImages,
                calibrationData.Map1x,
                calibrationData.Map1y,
                CancellationToken.None
            );
            GrayCodeDecodeResult secondaryCode = DecodeGrayCodeForCamera(
                scanImages.SecondaryImages,
                calibrationData.Map2x,
                calibrationData.Map2y,
                CancellationToken.None
            );
            LogStructuredLightDiagnostics(
                calibProjectId,
                roundIndex,
                "Main",
                mainCode
            );
            LogStructuredLightDiagnostics(
                calibProjectId,
                roundIndex,
                "Secondary",
                secondaryCode
            );

            int disparitySign = StereoReconstructionUtils.ComputeDisparitySign(
                calibrationData.ProjectionP1,
                calibrationData.ProjectionP2
            );
            using Mat disparity = BuildGrayCodeDisparity(
                mainCode,
                secondaryCode,
                disparitySign,
                out int matchedPixelCount,
                out GrayCodeMatchDiagnostics matchDiagnostics
            );
            LogStructuredLightMatchDiagnostics(
                calibProjectId,
                roundIndex,
                disparitySign,
                matchDiagnostics
            );
            if (matchedPixelCount == 0)
            {
                throw new InvalidOperationException(
                    $"第 {roundIndex} 轮多尺度条纹未找到有效双目对应点；"
                    + "请检查 20 帧顺序、投影曝光、相机同步和双目极线矫正。"
                );
            }

            using Mat depth = StereoReconstructionUtils.ComputeDepthFromDisparity(
                disparity,
                calibrationData.ProjectionP1,
                calibrationData.ProjectionP2,
                calibrationData.BaselineMm,
                disparitySign
            );
            DepthQualityPreview depthPreview = BuildDepthQualityPreview(depth);
            await _notifier.NotifyDepthQualityMapAsync(
                calibProjectId,
                depthPreview.PngBytes,
                depthPreview.ValidPointCount,
                depthPreview.TotalPointCount,
                depthPreview.MinimumDepthMm,
                depthPreview.MaximumDepthMm
            );
            if (matchedPixelCount < MinimumReliableStructuredLightMatches)
            {
                throw new InvalidOperationException(
                    $"第 {roundIndex} 轮结构光匹配点过少：{matchedPixelCount}/"
                    + $"{MinimumReliableStructuredLightMatches}。二维质量图已保留用于诊断，"
                    + $"主相机解码有效率={mainCode.ValidCount * 100d / mainCode.Valid.Length:F2}%，"
                    + $"从相机解码有效率={secondaryCode.ValidCount * 100d / secondaryCode.Valid.Length:F2}%。"
                    + "请优先改善从相机曝光、对焦和投影覆盖，并确认主从相机采集同一条纹帧。"
                );
            }

            _logger.LogInformation(
                "Step7 多尺度条纹解码匹配完成：ProjectId={ProjectId}, Round={Round}, MainValid={MainValid}, SecondaryValid={SecondaryValid}, Matched={Matched}, Direction={Direction}",
                calibProjectId,
                roundIndex,
                mainCode.ValidCount,
                secondaryCode.ValidCount,
                matchedPixelCount,
                disparitySign > 0 ? "Positive" : "Negative"
            );

            using (Mat textureMain = CalibImageUtils.LoadBgrMat(scanImages.TextureImage))
            using (Mat rectifiedTexture = new())
            {
                Cv2.Remap(
                    textureMain,
                    rectifiedTexture,
                    calibrationData.Map1x,
                    calibrationData.Map1y,
                    InterpolationFlags.Linear
                );

                var (pointCloud, colors) = StereoReconstructionUtils.GeneratePointCloud(
                    depth,
                    rectifiedTexture,
                    calibrationData.ProjectionP1
                );
                using (pointCloud)
                using (colors)
                {
                    byte[] plyBytes = StereoReconstructionUtils.WritePly(pointCloud, colors);

                    int totalPointCount =
                        _stateStore.GetTotalPointCount(calibProjectId) + pointCloud.Rows;
                    _stateStore.AddIncrementalPointCloud(
                        calibProjectId,
                        plyBytes,
                        pointCloud.Rows
                    );


                    PointCloudStatusDto? waiting = _stateStore.UpdateProgress(
                        calibProjectId,
                        0,
                        $"第 {roundIndex} 轮完成，等待下一轮采集"
                    );
                    if (waiting is not null)
                    {
                        await _notifier.NotifyStatusAsync(waiting);
                    }

                    _logger.LogInformation(
                        "Step7 增量彩色点云生成完成：ProjectId={ProjectId}, Round={Round}, PointCount={PointCount}, TotalPointCount={TotalPointCount}",
                        calibProjectId,
                        roundIndex,
                        pointCloud.Rows,
                        totalPointCount
                    );
                }
            }

            await unitOfWork.CompleteAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Step7 增量点云生成失败：ProjectId={ProjectId}, Round={Round}", calibProjectId, roundIndex);
            PointCloudStatusDto? failedRound = _stateStore.UpdateProgress(
                calibProjectId,
                0,
                $"第 {roundIndex} 轮重建失败：{ex.Message}"
            );
            if (failedRound is not null)
            {
                await _notifier.NotifyStatusAsync(failedRound);
            }
        }
    }

    public async Task GenerateIncrementalStereoPointCloudAsync(
        Guid calibProjectId,
        long roundIndex)
    {
        System.Diagnostics.Stopwatch roundTimer = System.Diagnostics.Stopwatch.StartNew();
        using IUnitOfWork unitOfWork = _unitOfWorkManager.Begin(
            requiresNew: true,
            isTransactional: false
        );
        try
        {
            CalibProject project = await _projectRepository.GetAsync(calibProjectId);
            if (!project.MainCameraDeviceId.HasValue || !project.SecondaryCameraDeviceId.HasValue)
            {
                throw new InvalidOperationException("普通双目点云生成需要绑定主、从两台相机");
            }

            if (!_stateStore.IsIncrementalModeActive(calibProjectId))
            {
                _stateStore.StartIncrementalMode(calibProjectId);
            }

            CalibrationData? calibrationData = await LoadCalibrationDataAsync(
                project,
                CancellationToken.None,
                requireProjectorParameters: false
            );
            if (calibrationData is null)
            {
                throw new InvalidOperationException("双目标定数据不完整，无法生成普通双目点云");
            }

            _logger.LogInformation(
                "Step7 普通双目重建开始：ProjectId={ProjectId}, Round={Round}, MainCamera={MainCamera}, SecondaryCamera={SecondaryCamera}, "
                + "Baseline={Baseline:F3}, Fx={Fx:F3}, Fy={Fy:F3}, Cx={Cx:F3}, Cy={Cy:F3}, RectifyMap={MapWidth}x{MapHeight}",
                calibProjectId,
                roundIndex,
                project.MainCameraDeviceId,
                project.SecondaryCameraDeviceId,
                calibrationData.BaselineMm,
                calibrationData.ProjectionP1.At<double>(0, 0),
                calibrationData.ProjectionP1.At<double>(1, 1),
                calibrationData.ProjectionP1.At<double>(0, 2),
                calibrationData.ProjectionP1.At<double>(1, 2),
                calibrationData.Map1x.Cols,
                calibrationData.Map1x.Rows
            );

            string mainKey = CalibScanAppService.BuildScanBlobKey(
                project.Id,
                project.MainCameraDeviceId.Value,
                roundIndex,
                0,
                CalibScanCameraRole.Main
            );
            string secondaryKey = CalibScanAppService.BuildScanBlobKey(
                project.Id,
                project.SecondaryCameraDeviceId.Value,
                roundIndex,
                0,
                CalibScanCameraRole.Secondary
            );
            if (
                !await _blobContainer.ExistsAsync(mainKey)
                || !await _blobContainer.ExistsAsync(secondaryKey)
            )
            {
                throw new InvalidOperationException($"第 {roundIndex} 轮普通双目图像不完整");
            }

            byte[] mainBytes = await _blobContainer.GetAllBytesAsync(mainKey);
            byte[] secondaryBytes = await _blobContainer.GetAllBytesAsync(secondaryKey);

            PointCloudStatusDto? reconstructing = _stateStore.UpdateProgress(
                calibProjectId,
                50,
                $"正在重建第 {roundIndex} 轮普通双目点云"
            );
            if (reconstructing is not null)
            {
                await _notifier.NotifyStatusAsync(reconstructing);
            }

            using Mat mainImage = CalibImageUtils.LoadBgrMat(mainBytes);
            using Mat secondaryImage = CalibImageUtils.LoadBgrMat(secondaryBytes);
            _logger.LogInformation(
                "Step7 普通双目图像已加载：ProjectId={ProjectId}, Round={Round}, Main={MainWidth}x{MainHeight}/{MainBytes}Bytes, "
                + "Secondary={SecondaryWidth}x{SecondaryHeight}/{SecondaryBytes}Bytes",
                calibProjectId,
                roundIndex,
                mainImage.Cols,
                mainImage.Rows,
                mainBytes.Length,
                secondaryImage.Cols,
                secondaryImage.Rows,
                secondaryBytes.Length
            );
            var (rectifiedMain, rectifiedSecondary) = StereoReconstructionUtils.RectifyImages(
                mainImage,
                secondaryImage,
                calibrationData.Map1x,
                calibrationData.Map1y,
                calibrationData.Map2x,
                calibrationData.Map2y
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
                var mainStats = CalculateGrayImageStats(mainGray);
                var secondaryStats = CalculateGrayImageStats(secondaryGray);
                _logger.LogInformation(
                    "Step7 普通双目矫正图统计：ProjectId={ProjectId}, Round={Round}, "
                    + "MainRange=[{MainMin:F0},{MainMax:F0}], MainMean={MainMean:F2}, MainStdDev={MainStdDev:F2}, MainNonBlack={MainNonBlack:P2}; "
                    + "SecondaryRange=[{SecondaryMin:F0},{SecondaryMax:F0}], SecondaryMean={SecondaryMean:F2}, "
                    + "SecondaryStdDev={SecondaryStdDev:F2}, SecondaryNonBlack={SecondaryNonBlack:P2}",
                    calibProjectId,
                    roundIndex,
                    mainStats.Min,
                    mainStats.Max,
                    mainStats.Mean,
                    mainStats.StdDev,
                    mainStats.NonBlackRatio,
                    secondaryStats.Min,
                    secondaryStats.Max,
                    secondaryStats.Mean,
                    secondaryStats.StdDev,
                    secondaryStats.NonBlackRatio
                );

                int disparitySign = StereoReconstructionUtils.ComputeDisparitySign(
                    calibrationData.ProjectionP1,
                    calibrationData.ProjectionP2
                );
                int minDisparity = disparitySign > 0 ? 0 : -128;
                _logger.LogInformation(
                    "Step7 普通双目极线矫正完成：ProjectId={ProjectId}, Round={Round}, Size={Width}x{Height}, "
                    + "DisparityDirection={Direction}, Search=[{SearchMin},{SearchMax}], BlockSize=15",
                    calibProjectId,
                    roundIndex,
                    rectifiedMain.Cols,
                    rectifiedMain.Rows,
                    disparitySign > 0 ? "Positive" : "Negative",
                    minDisparity,
                    minDisparity + 127
                );
                using StereoBM matcher = StereoBM.Create(numDisparities: 128, blockSize: 15);
                matcher.MinDisparity = minDisparity;
                matcher.Compute(mainGray, secondaryGray, disparity16);
                disparity16.ConvertTo(disparity, MatType.CV_64FC1, 1d / 16d);

                Cv2.MinMaxLoc(disparity, out double minObservedDisparity, out double maxObservedDisparity);
                int validDisparityPixels = CountValidDisparities(
                    disparity,
                    disparitySign,
                    minDisparity,
                    128
                );
                long totalPixels = (long)disparity.Rows * disparity.Cols;
                _logger.LogInformation(
                    "Step7 普通双目视差统计：ProjectId={ProjectId}, Round={Round}, Direction={Direction}, "
                    + "Range=[{Min:F2},{Max:F2}], Valid={Valid}/{Total} ({ValidRatio:P2})",
                    calibProjectId,
                    roundIndex,
                    disparitySign > 0 ? "Positive" : "Negative",
                    minObservedDisparity,
                    maxObservedDisparity,
                    validDisparityPixels,
                    totalPixels,
                    totalPixels == 0 ? 0d : (double)validDisparityPixels / totalPixels
                );

                int oppositeValidDisparityPixels = 0;
                if (validDisparityPixels == 0)
                {
                    int oppositeSign = -disparitySign;
                    int oppositeMinDisparity = oppositeSign > 0 ? 0 : -128;
                    using Mat oppositeDisparity16 = new();
                    using Mat oppositeDisparity = new();
                    using StereoBM oppositeMatcher = StereoBM.Create(
                        numDisparities: 128,
                        blockSize: 15
                    );
                    oppositeMatcher.MinDisparity = oppositeMinDisparity;
                    oppositeMatcher.Compute(mainGray, secondaryGray, oppositeDisparity16);
                    oppositeDisparity16.ConvertTo(
                        oppositeDisparity,
                        MatType.CV_64FC1,
                        1d / 16d
                    );
                    oppositeValidDisparityPixels = CountValidDisparities(
                        oppositeDisparity,
                        oppositeSign,
                        oppositeMinDisparity,
                        128
                    );
                    Cv2.MinMaxLoc(
                        oppositeDisparity,
                        out double oppositeMinObserved,
                        out double oppositeMaxObserved
                    );
                    _logger.LogWarning(
                        "Step7 普通双目反方向探测：ProjectId={ProjectId}, Round={Round}, Direction={Direction}, "
                        + "Search=[{SearchMin},{SearchMax}], Range=[{Min:F2},{Max:F2}], Valid={Valid}/{Total} ({ValidRatio:P2})。"
                        + "该结果仅用于诊断，不参与点云生成。",
                        calibProjectId,
                        roundIndex,
                        oppositeSign > 0 ? "Positive" : "Negative",
                        oppositeMinDisparity,
                        oppositeMinDisparity + 127,
                        oppositeMinObserved,
                        oppositeMaxObserved,
                        oppositeValidDisparityPixels,
                        totalPixels,
                        totalPixels == 0
                            ? 0d
                            : (double)oppositeValidDisparityPixels / totalPixels
                    );
                }

                using Mat depth = StereoReconstructionUtils.ComputeDepthFromDisparity(
                    disparity,
                    calibrationData.ProjectionP1,
                    calibrationData.ProjectionP2,
                    calibrationData.BaselineMm,
                    disparitySign
                );
                DepthQualityPreview depthPreview = BuildDepthQualityPreview(depth);
                await _notifier.NotifyDepthQualityMapAsync(
                    calibProjectId,
                    depthPreview.PngBytes,
                    depthPreview.ValidPointCount,
                    depthPreview.TotalPointCount,
                    depthPreview.MinimumDepthMm,
                    depthPreview.MaximumDepthMm
                );
                var (pointCloud, colors) = StereoReconstructionUtils.GeneratePointCloud(
                    depth,
                    rectifiedMain,
                    calibrationData.ProjectionP1,
                    sampleStep: 8
                );
                using (pointCloud)
                using (colors)
                {
                    _logger.LogInformation(
                        "Step7 普通双目点云统计：ProjectId={ProjectId}, Round={Round}, ValidDisparity={ValidDisparity}, "
                        + "SampleStep=8, GeneratedPoints={GeneratedPoints}, ElapsedMs={ElapsedMs}",
                        calibProjectId,
                        roundIndex,
                        validDisparityPixels,
                        pointCloud.Rows,
                        roundTimer.ElapsedMilliseconds
                    );
                    if (pointCloud.Rows == 0)
                    {
                        throw new InvalidOperationException(
                            $"第 {roundIndex} 轮未得到有效双目点云。"
                            + $"视差范围=[{minObservedDisparity:F2}, {maxObservedDisparity:F2}]，"
                            + $"反方向有效视差={oppositeValidDisparityPixels}，"
                            + $"矫正图非黑比例=主{mainStats.NonBlackRatio:P2}/从{secondaryStats.NonBlackRatio:P2}。"
                            + "请检查主从相机顺序、双目标定、曝光同步以及被测物表面纹理。"
                        );
                    }

                    byte[] plyBytes = StereoReconstructionUtils.WritePly(pointCloud, colors);
                    int totalPointCount =
                        _stateStore.GetTotalPointCount(calibProjectId) + pointCloud.Rows;
                    _stateStore.AddIncrementalPointCloud(
                        calibProjectId,
                        plyBytes,
                        pointCloud.Rows
                    );
                    PointCloudStatusDto? waiting = _stateStore.UpdateProgress(
                        calibProjectId,
                        0,
                        $"第 {roundIndex} 轮普通双目点云完成，等待下一帧"
                    );
                    if (waiting is not null)
                    {
                        await _notifier.NotifyStatusAsync(waiting);
                    }

                    _logger.LogInformation(
                        "Step7 普通双目增量点云完成：ProjectId={ProjectId}, Round={Round}, AddedPoints={AddedPoints}, "
                        + "TotalPoints={TotalPoints}, PlyBytes={PlyBytes}, ElapsedMs={ElapsedMs}",
                        calibProjectId,
                        roundIndex,
                        pointCloud.Rows,
                        totalPointCount,
                        plyBytes.Length,
                        roundTimer.ElapsedMilliseconds
                    );
                }
            }

            await unitOfWork.CompleteAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Step7 普通双目增量点云生成失败：ProjectId={ProjectId}, Round={Round}",
                calibProjectId,
                roundIndex
            );
            PointCloudStatusDto? failedRound = _stateStore.UpdateProgress(
                calibProjectId,
                0,
                $"第 {roundIndex} 轮普通双目重建失败：{ex.Message}"
            );
            if (failedRound is not null)
            {
                await _notifier.NotifyStatusAsync(failedRound);
            }
        }
    }

    private static (
        double Min,
        double Max,
        double Mean,
        double StdDev,
        double NonBlackRatio
    ) CalculateGrayImageStats(Mat gray)
    {
        Cv2.MinMaxLoc(gray, out double min, out double max);
        Cv2.MeanStdDev(gray, out Scalar mean, out Scalar stdDev);
        using Mat nonBlackMask = new();
        Cv2.Compare(gray, 1, nonBlackMask, CmpTypes.GT);
        long totalPixels = (long)gray.Rows * gray.Cols;
        double nonBlackRatio =
            totalPixels == 0 ? 0d : (double)Cv2.CountNonZero(nonBlackMask) / totalPixels;
        return (min, max, mean.Val0, stdDev.Val0, nonBlackRatio);
    }

    private static int CountValidDisparities(
        Mat disparity,
        int disparitySign,
        int minDisparity,
        int numDisparities
    )
    {
        double lower = disparitySign > 0 ? 0.1 : minDisparity;
        double upper = disparitySign > 0
            ? minDisparity + numDisparities - 1
            : -0.1;
        using Mat validMask = new();
        Cv2.InRange(disparity, new Scalar(lower), new Scalar(upper), validMask);
        return Cv2.CountNonZero(validMask);
    }

    public async Task CompleteIncrementalPointCloudAsync(Guid calibProjectId)
    {
        try
        {
            PointCloudSessionState? session = _stateStore.CompleteIncrementalMode(calibProjectId);
            if (session == null)
            {
                return;
            }

            List<byte[]> chunks = _stateStore.GetAccumulatedPointCloudChunks(calibProjectId);
            if (chunks.Count == 0)
            {
                await _notifier.NotifyStatusAsync(session.ToDto());
                return;
            }

            byte[] mergedPly = MergePlyFiles(chunks);

            string plyBlobKey = $"{calibProjectId}/pointcloud/{DateTime.UtcNow:yyyyMMddHHmmss}.ply";
            await _blobContainer.SaveAsync(plyBlobKey, mergedPly, overrideExisting: false);

            string plyDownloadUrl = $"/api/app/calib-point-cloud/download/{calibProjectId}";

            session.PlyDownloadUrl = plyDownloadUrl;
            session.PlyFileSizeBytes = mergedPly.Length;
            session.PlyBlobKey = plyBlobKey;

            await _notifier.NotifyStatusAsync(session.ToDto());

            _logger.LogInformation(
                "Step7 增量点云合并完成：ProjectId={ProjectId}, TotalPointCount={TotalPointCount}, FileSize={FileSize}",
                calibProjectId,
                session.TotalPointCount,
                mergedPly.Length
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Step7 增量点云合并失败：ProjectId={ProjectId}", calibProjectId);
            PointCloudStatusDto failed = _stateStore.Fail(calibProjectId, ex.Message);
            await _notifier.NotifyStatusAsync(failed);
        }
    }

    private async Task<ScanImages> LoadScanImagesForRoundAsync(
        CalibProject project,
        long roundIndex,
        int totalFrames)
    {
        ScanImages result = new();

        for (int frame = 0; frame < totalFrames; frame++)
        {
            if (project.MainCameraDeviceId.HasValue)
            {
                string mainKey = CalibScanAppService.BuildScanBlobKey(
                    project.Id,
                    project.MainCameraDeviceId.Value,
                    roundIndex,
                    frame,
                    CalibScanCameraRole.Main
                );

                if (await _blobContainer.ExistsAsync(mainKey))
                {
                    byte[] bytes = await _blobContainer.GetAllBytesAsync(mainKey);
                    result.MainImages.Add(bytes);
                }
            }

            if (project.SecondaryCameraDeviceId.HasValue)
            {
                string secondaryKey = CalibScanAppService.BuildScanBlobKey(
                    project.Id,
                    project.SecondaryCameraDeviceId.Value,
                    roundIndex,
                    frame,
                    CalibScanCameraRole.Secondary
                );

                if (await _blobContainer.ExistsAsync(secondaryKey))
                {
                    byte[] bytes = await _blobContainer.GetAllBytesAsync(secondaryKey);
                    result.SecondaryImages.Add(bytes);
                }
            }
        }

        if (project.MainCameraDeviceId.HasValue)
        {
            string textureKey = CalibScanAppService.BuildTextureBlobKey(
                project.Id,
                project.MainCameraDeviceId.Value,
                roundIndex
            );
            if (await _blobContainer.ExistsAsync(textureKey))
            {
                result.TextureImage = await _blobContainer.GetAllBytesAsync(textureKey);
            }
        }

        return result;
    }

    private static GrayCodeDecodeResult DecodeGrayCodeForCamera(
        List<byte[]> images,
        Mat mapX,
        Mat mapY,
        CancellationToken cancellationToken)
    {
        if (images.Count != GrayCodePatternLayout.TotalFrameCount)
        {
            throw new InvalidOperationException(
                $"多尺度条纹解码需要 {GrayCodePatternLayout.TotalFrameCount} 帧，实际 {images.Count} 帧"
            );
        }

        int width = mapX.Cols;
        int height = mapX.Rows;
        int pixelCount = checked(width * height);
        byte[] valid = Enumerable.Repeat((byte)1, pixelCount).ToArray();
        int[] grayX = new int[pixelCount];
        int[] grayY = new int[pixelCount];
        List<GrayCodePairDiagnostics> pairDiagnostics = [];

        DecodeGrayBits(
            images,
            0,
            GrayCodePatternLayout.HorizontalBitCount,
            grayY,
            valid,
            mapX,
            mapY,
            pairDiagnostics,
            cancellationToken
        );
        DecodeGrayBits(
            images,
            GrayCodePatternLayout.HorizontalFrameCount,
            GrayCodePatternLayout.VerticalBitCount,
            grayX,
            valid,
            mapX,
            mapY,
            pairDiagnostics,
            cancellationToken
        );

        int validCount = 0;
        for (int i = 0; i < pixelCount; i++)
        {
            if (valid[i] == 0)
            {
                continue;
            }

            validCount++;
        }

        return new GrayCodeDecodeResult(
            width,
            height,
            grayX,
            grayY,
            valid,
            validCount,
            pairDiagnostics
        );
    }

    private static void DecodeGrayBits(
        List<byte[]> images,
        int firstFrame,
        int bitCount,
        int[] grayValues,
        byte[] valid,
        Mat mapX,
        Mat mapY,
        List<GrayCodePairDiagnostics> diagnostics,
        CancellationToken cancellationToken)
    {
        for (int pair = 0; pair < bitCount; pair++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] original = ReadRectifiedGray(images[firstFrame + pair * 2], mapX, mapY);
            byte[] inverse = ReadRectifiedGray(images[firstFrame + pair * 2 + 1], mapX, mapY);
            long originalSum = 0;
            long inverseSum = 0;
            long absoluteDifferenceSum = 0;
            int strongDifferenceCount = 0;
            int positiveDifferenceCount = 0;
            for (int i = 0; i < valid.Length; i++)
            {
                originalSum += original[i];
                inverseSum += inverse[i];
                int pairDifference = original[i] - inverse[i];
                int absoluteDifference = Math.Abs(pairDifference);
                absoluteDifferenceSum += absoluteDifference;
                if (absoluteDifference >= 5)
                {
                    strongDifferenceCount++;
                    if (pairDifference > 0)
                    {
                        positiveDifferenceCount++;
                    }
                }

                if (valid[i] == 0)
                {
                    continue;
                }

                int difference = pairDifference;
                if (Math.Abs(difference) < 5)
                {
                    valid[i] = 0;
                    continue;
                }

                grayValues[i] = (grayValues[i] << 1) | (difference > 0 ? 1 : 0);
            }

            int frameIndex = firstFrame + pair * 2;
            diagnostics.Add(
                new GrayCodePairDiagnostics(
                    frameIndex,
                    GrayCodePatternLayout.GetFrameLabel(frameIndex),
                    originalSum / (double)valid.Length,
                    inverseSum / (double)valid.Length,
                    absoluteDifferenceSum / (double)valid.Length,
                    strongDifferenceCount,
                    positiveDifferenceCount,
                    valid.Length
                )
            );
        }
    }

    private static byte[] ReadRectifiedGray(byte[] imageBytes, Mat mapX, Mat mapY)
    {
        using Mat bgr = CalibImageUtils.LoadBgrMat(imageBytes);
        using Mat gray = new();
        using Mat rectified = new();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.Remap(gray, rectified, mapX, mapY, InterpolationFlags.Linear);
        byte[] pixels = new byte[checked(rectified.Rows * rectified.Cols)];
        System.Runtime.InteropServices.Marshal.Copy(rectified.Data, pixels, 0, pixels.Length);
        return pixels;
    }

    private static Mat BuildGrayCodeDisparity(
        GrayCodeDecodeResult main,
        GrayCodeDecodeResult secondary,
        int disparitySign,
        out int matchedPixelCount,
        out GrayCodeMatchDiagnostics diagnostics)
    {
        if (main.Width != secondary.Width || main.Height != secondary.Height)
        {
            throw new InvalidOperationException("主从相机条纹解码图尺寸不一致。");
        }

        int pixelCount = checked(main.Width * main.Height);
        double[] disparities = Enumerable.Repeat(double.NaN, pixelCount).ToArray();
        matchedPixelCount = 0;
        long mainValidPixels = 0;
        long codeFoundPixels = 0;
        long candidateCount = 0;
        long nonPositiveRejected = 0;
        long overRangeRejected = 0;
        long ambiguousPixelCount = 0;
        long rowsWithSecondaryCodes = 0;
        double minimumPositiveCandidate = double.PositiveInfinity;
        double maximumPositiveCandidate = double.NegativeInfinity;

        for (int y = 0; y < main.Height; y++)
        {
            int rowOffset = y * main.Width;
            Dictionary<int, List<int>> secondaryByPatternCode = new();
            for (int x = 0; x < secondary.Width; x++)
            {
                int index = rowOffset + x;
                if (secondary.Valid[index] == 0)
                {
                    continue;
                }

                int key = (secondary.ProjectorY[index] << 5) | secondary.ProjectorX[index];
                if (secondaryByPatternCode.TryGetValue(key, out List<int>? candidates))
                {
                    candidates.Add(x);
                }
                else
                {
                    secondaryByPatternCode[key] = [x];
                }
            }
            if (secondaryByPatternCode.Count > 0)
            {
                rowsWithSecondaryCodes++;
            }

            for (int x = 0; x < main.Width; x++)
            {
                int index = rowOffset + x;
                if (main.Valid[index] == 0)
                {
                    continue;
                }
                mainValidPixels++;

                int key = (main.ProjectorY[index] << 5) | main.ProjectorX[index];
                if (!secondaryByPatternCode.TryGetValue(key, out List<int>? candidates))
                {
                    continue;
                }
                codeFoundPixels++;
                candidateCount += candidates.Count;

                double bestDisparity = double.NaN;
                double bestMagnitude = double.MaxValue;
                int admissibleCandidateCount = 0;
                foreach (int secondaryX in candidates)
                {
                    double candidateDisparity = x - secondaryX;
                    double signed = candidateDisparity * disparitySign;
                    if (signed <= 0.1)
                    {
                        nonPositiveRejected++;
                        continue;
                    }
                    minimumPositiveCandidate = Math.Min(minimumPositiveCandidate, signed);
                    maximumPositiveCandidate = Math.Max(maximumPositiveCandidate, signed);
                    if (signed > MaxStructuredLightDisparity)
                    {
                        overRangeRejected++;
                        continue;
                    }
                    admissibleCandidateCount++;
                    if (signed >= bestMagnitude)
                    {
                        continue;
                    }

                    bestDisparity = candidateDisparity;
                    bestMagnitude = signed;
                }

                // 当前 5 位竖条纹编码每 128px 重复一次。若同一极线上有多个合法
                // 候选，任取“最小视差”会生成与真实物体无关的周期别名点云。
                if (admissibleCandidateCount > 1)
                {
                    ambiguousPixelCount++;
                    continue;
                }
                if (admissibleCandidateCount != 1 || !double.IsFinite(bestDisparity))
                {
                    continue;
                }

                disparities[index] = bestDisparity;
                matchedPixelCount++;
            }
        }
        diagnostics = new GrayCodeMatchDiagnostics(
            mainValidPixels,
            codeFoundPixels,
            candidateCount,
            nonPositiveRejected,
            overRangeRejected,
            ambiguousPixelCount,
            matchedPixelCount,
            rowsWithSecondaryCodes,
            double.IsFinite(minimumPositiveCandidate) ? minimumPositiveCandidate : double.NaN,
            double.IsFinite(maximumPositiveCandidate) ? maximumPositiveCandidate : double.NaN
        );

        Mat result = new(main.Height, main.Width, MatType.CV_64FC1);
        System.Runtime.InteropServices.Marshal.Copy(
            disparities,
            0,
            result.Data,
            disparities.Length
        );
        return result;
    }

    private byte[] MergePlyFiles(List<byte[]> plyChunks)
    {
        if (plyChunks.Count == 0)
        {
            return Array.Empty<byte>();
        }

        if (plyChunks.Count == 1)
        {
            return plyChunks[0];
        }

        int totalVertexCount = 0;
        List<(byte[] Chunk, int DataOffset)> chunkData = new(plyChunks.Count);

        foreach (byte[] chunk in plyChunks)
        {
            string content = Encoding.ASCII.GetString(chunk);
            int headerEnd = content.IndexOf("end_header", StringComparison.Ordinal);
            if (headerEnd < 0)
            {
                throw new InvalidDataException("增量点云块缺少 PLY end_header。");
            }

            int vertexLineStart = content.IndexOf("element vertex ", StringComparison.Ordinal);
            int vertexLineEnd = vertexLineStart >= 0 ? content.IndexOf('\n', vertexLineStart) : -1;
            if (vertexLineStart < 0
                || vertexLineEnd < 0
                || !int.TryParse(
                    content.AsSpan(vertexLineStart + "element vertex ".Length,
                        vertexLineEnd - vertexLineStart - "element vertex ".Length).Trim(),
                    out int vertexCount))
            {
                throw new InvalidDataException("增量点云块的 PLY 顶点数量无效。");
            }

            totalVertexCount = checked(totalVertexCount + vertexCount);
            int dataOffset = headerEnd + "end_header".Length;
            while (dataOffset < chunk.Length && (chunk[dataOffset] == (byte)'\r' || chunk[dataOffset] == (byte)'\n'))
            {
                dataOffset++;
            }
            chunkData.Add((chunk, dataOffset));
        }

        string header =
            "ply\n"
            + "format ascii 1.0\n"
            + $"element vertex {totalVertexCount}\n"
            + "property float x\n"
            + "property float y\n"
            + "property float z\n"
            + "property uchar red\n"
            + "property uchar green\n"
            + "property uchar blue\n"
            + "end_header\n";
        int capacity = checked(
            Encoding.ASCII.GetByteCount(header)
            + chunkData.Sum(x => x.Chunk.Length - x.DataOffset + 1)
        );
        using MemoryStream merged = new(capacity);
        merged.Write(Encoding.ASCII.GetBytes(header));
        foreach ((byte[] chunk, int dataOffset) in chunkData)
        {
            merged.Write(chunk, dataOffset, chunk.Length - dataOffset);
            if (chunk.Length == dataOffset || chunk[^1] != (byte)'\n')
            {
                merged.WriteByte((byte)'\n');
            }
        }

        return merged.ToArray();
    }

    private async Task RunGenerationAsync(CalibProject project, CancellationToken cancellationToken)
    {
        try
        {
            await ReportProgressAsync(project.Id, 5, "正在加载标定参数...", cancellationToken);

            var calibrationData = await LoadCalibrationDataAsync(project, cancellationToken);
            if (calibrationData == null)
            {
                throw new UserFriendlyException("标定数据不完整，请先完成相机标定和双目标定");
            }

            await ReportProgressAsync(project.Id, 10, "标定参数加载完成", cancellationToken);

            await ReportProgressAsync(project.Id, 15, "正在加载扫描图像...", cancellationToken);

            var scanImages = await LoadScanImagesAsync(
                project,
                GrayCodePatternLayout.TotalFrameCount,
                cancellationToken
            );
            if (scanImages.MainImages.Count == 0 || scanImages.SecondaryImages.Count == 0)
            {
                throw new UserFriendlyException("未找到扫描图像，请先执行在线扫描采集");
            }

            await ReportProgressAsync(project.Id, 25, "扫描图像加载完成，共 {MainCount} 张主相机图，{SecondaryCount} 张从相机图", cancellationToken,
                scanImages.MainImages.Count, scanImages.SecondaryImages.Count);

            await ReportProgressAsync(project.Id, 30, "正在解码多尺度互补条纹...", cancellationToken);

            GrayCodeDecodeResult mainCode = DecodeGrayCodeForCamera(
                scanImages.MainImages,
                calibrationData.Map1x,
                calibrationData.Map1y,
                cancellationToken
            );
            GrayCodeDecodeResult secondaryCode = DecodeGrayCodeForCamera(
                scanImages.SecondaryImages,
                calibrationData.Map2x,
                calibrationData.Map2y,
                cancellationToken
            );
            LogStructuredLightDiagnostics(project.Id, 1, "Main", mainCode);
            LogStructuredLightDiagnostics(project.Id, 1, "Secondary", secondaryCode);

            await ReportProgressAsync(project.Id, 50, "多尺度条纹解码完成", cancellationToken);
            await ReportProgressAsync(project.Id, 55, "正在按投影坐标进行立体匹配...", cancellationToken);

            int disparitySign = StereoReconstructionUtils.ComputeDisparitySign(
                calibrationData.ProjectionP1,
                calibrationData.ProjectionP2
            );
            using Mat disparity = BuildGrayCodeDisparity(
                mainCode,
                secondaryCode,
                disparitySign,
                out int matchedPixelCount,
                out GrayCodeMatchDiagnostics matchDiagnostics
            );
            LogStructuredLightMatchDiagnostics(project.Id, 1, disparitySign, matchDiagnostics);
            if (matchedPixelCount == 0)
            {
                throw new UserFriendlyException("多尺度条纹未找到有效双目对应点，请检查采集帧序与曝光。");
            }

            await ReportProgressAsync(project.Id, 65, "立体匹配完成", cancellationToken);

            await ReportProgressAsync(project.Id, 70, "正在计算深度图...", cancellationToken);

            double baselineMm = calibrationData.BaselineMm;
            using Mat depth = StereoReconstructionUtils.ComputeDepthFromDisparity(
                disparity,
                calibrationData.ProjectionP1,
                calibrationData.ProjectionP2,
                baselineMm,
                disparitySign
            );

            await ReportProgressAsync(project.Id, 80, "深度图计算完成", cancellationToken);

            await ReportProgressAsync(project.Id, 85, "正在生成点云...", cancellationToken);

            byte[] colorSource = scanImages.TextureImage ?? scanImages.MainImages[0];
            using (Mat rectifiedMain = CalibImageUtils.LoadBgrMat(colorSource))
            using (Mat rectified = new())
            {
                Cv2.Remap(rectifiedMain, rectified, calibrationData.Map1x, calibrationData.Map1y, InterpolationFlags.Linear);

                var (pointCloud, colors) = StereoReconstructionUtils.GeneratePointCloud(
                    depth,
                    rectified,
                    calibrationData.ProjectionP1
                );

                await ReportProgressAsync(project.Id, 90, "点云生成完成，正在保存 PLY 文件...", cancellationToken);

                byte[] plyBytes = StereoReconstructionUtils.WritePly(pointCloud, colors);

                string plyBlobKey = $"{project.Id}/pointcloud/{DateTime.UtcNow:yyyyMMddHHmmss}.ply";
                await _blobContainer.SaveAsync(plyBlobKey, plyBytes, overrideExisting: false);

                string plyDownloadUrl = $"/api/app/calib-point-cloud/download/{project.Id}";

                PointCloudStatusDto completed = _stateStore.Complete(
                    project.Id,
                    plyDownloadUrl,
                    plyBytes.Length,
                    plyBlobKey
                );
                await _notifier.NotifyStatusAsync(completed);

                _logger.LogInformation(
                    "Step7 点云生成完成：ProjectId={ProjectId}, PointCount={PointCount}, FileSize={FileSize} bytes",
                    project.Id,
                    pointCloud.Rows,
                    plyBytes.Length
                );
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Step7 点云生成被取消：ProjectId={ProjectId}", project.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Step7 点云生成失败：ProjectId={ProjectId}", project.Id);
            PointCloudStatusDto failed = _stateStore.Fail(project.Id, ex.Message);
            try
            {
                await _notifier.NotifyStatusAsync(failed);
            }
            catch
            {
                // 通知失败时不再抛出
            }
        }
    }

    private async Task<CalibrationData?> LoadCalibrationDataAsync(
        CalibProject project,
        CancellationToken cancellationToken,
        bool requireProjectorParameters = true)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IQueryable<CalibCameraParam> camParamQuery = await _cameraParamRepository.GetQueryableAsync();
        CalibCameraParam? mainCamParam = await AsyncExecuter.FirstOrDefaultAsync(
            camParamQuery.Where(x => x.CalibProjectId == project.Id && x.CameraDeviceId == project.MainCameraDeviceId)
        );

        if (mainCamParam == null || string.IsNullOrEmpty(mainCamParam.IntrinsicMatrixJson))
        {
            return null;
        }

        IQueryable<CalibStereoResult> stereoQuery = await _stereoResultRepository.GetQueryableAsync();
        CalibStereoResult? stereoResult = await AsyncExecuter.FirstOrDefaultAsync(
            stereoQuery.Where(x => x.CalibProjectId == project.Id)
        );

        if (stereoResult == null)
        {
            return null;
        }

        IQueryable<CalibProjectorParam> projParamQuery = await _projectorParamRepository.GetQueryableAsync();
        CalibProjectorParam? projParam = await AsyncExecuter.FirstOrDefaultAsync(
            projParamQuery.Where(x => x.CalibProjectId == project.Id)
        );

        if (requireProjectorParameters && (projParam == null || projParam.PatternCount <= 0))
        {
            return null;
        }

        using Mat projectionP1 = CalibImageUtils.DeserializeMatrix(
            stereoResult.ProjectionP1Json,
            3,
            4
        );
        using Mat projectionP2 = CalibImageUtils.DeserializeMatrix(
            stereoResult.ProjectionP2Json,
            3,
            4
        );

        using Mat baseline = StereoReconstructionUtils.ComputeBaselineFromStereoResult(projectionP1, projectionP2);
        double baselineMm = StereoReconstructionUtils.ComputeBaselineDistance(baseline);

        if (stereoResult.RectifyMapWidth <= 0 || stereoResult.RectifyMapHeight <= 0)
        {
            throw new UserFriendlyException(
                $"双目标定矫正图尺寸无效：{stereoResult.RectifyMapWidth}x{stereoResult.RectifyMapHeight}，请重新执行双目标定。"
            );
        }

        Mat map1x = new(stereoResult.RectifyMapHeight, stereoResult.RectifyMapWidth, MatType.CV_32FC1);
        Mat map1y = new(stereoResult.RectifyMapHeight, stereoResult.RectifyMapWidth, MatType.CV_32FC1);
        Mat map2x = new(stereoResult.RectifyMapHeight, stereoResult.RectifyMapWidth, MatType.CV_32FC1);
        Mat map2y = new(stereoResult.RectifyMapHeight, stereoResult.RectifyMapWidth, MatType.CV_32FC1);

        bool mapsExist =
            await _blobContainer.ExistsAsync(stereoResult.Map1XBlobKey)
            && await _blobContainer.ExistsAsync(stereoResult.Map1YBlobKey)
            && await _blobContainer.ExistsAsync(stereoResult.Map2XBlobKey)
            && await _blobContainer.ExistsAsync(stereoResult.Map2YBlobKey);

        if (mapsExist)
        {
            byte[] map1xBytes = await _blobContainer.GetAllBytesAsync(stereoResult.Map1XBlobKey);
            byte[] map1yBytes = await _blobContainer.GetAllBytesAsync(stereoResult.Map1YBlobKey);
            byte[] map2xBytes = await _blobContainer.GetAllBytesAsync(stereoResult.Map2XBlobKey);
            byte[] map2yBytes = await _blobContainer.GetAllBytesAsync(stereoResult.Map2YBlobKey);

            int expectedMapBytes = checked(
                stereoResult.RectifyMapWidth
                    * stereoResult.RectifyMapHeight
                    * sizeof(float)
            );
            mapsExist =
                map1xBytes.Length == expectedMapBytes
                && map1yBytes.Length == expectedMapBytes
                && map2xBytes.Length == expectedMapBytes
                && map2yBytes.Length == expectedMapBytes;
            if (mapsExist)
            {
                System.Runtime.InteropServices.Marshal.Copy(map1xBytes, 0, map1x.Data, expectedMapBytes);
                System.Runtime.InteropServices.Marshal.Copy(map1yBytes, 0, map1y.Data, expectedMapBytes);
                System.Runtime.InteropServices.Marshal.Copy(map2xBytes, 0, map2x.Data, expectedMapBytes);
                System.Runtime.InteropServices.Marshal.Copy(map2yBytes, 0, map2y.Data, expectedMapBytes);
            }
            else
            {
                _logger.LogWarning(
                    "双目矫正映射 Blob 尺寸异常，将自动重建：ProjectId={ProjectId}, ExpectedBytes={ExpectedBytes}",
                    project.Id,
                    expectedMapBytes
                );
            }
        }

        if (!mapsExist)
        {
            CalibCameraParam? secondaryCamParam = await AsyncExecuter.FirstOrDefaultAsync(
                camParamQuery.Where(
                    x =>
                        x.CalibProjectId == project.Id
                        && x.CameraDeviceId == project.SecondaryCameraDeviceId
                )
            );
            if (
                secondaryCamParam is null
                || string.IsNullOrWhiteSpace(mainCamParam.DistCoeffsJson)
                || string.IsNullOrWhiteSpace(secondaryCamParam.IntrinsicMatrixJson)
                || string.IsNullOrWhiteSpace(secondaryCamParam.DistCoeffsJson)
            )
            {
                throw new InvalidOperationException(
                    "双目矫正映射文件缺失，且主从相机内参/畸变参数不完整，无法自动重建"
                );
            }

            using Mat mainMatrix = CalibImageUtils.DeserializeMatrix(
                mainCamParam.IntrinsicMatrixJson,
                3,
                3
            );
            using Mat secondaryMatrix = CalibImageUtils.DeserializeMatrix(
                secondaryCamParam.IntrinsicMatrixJson,
                3,
                3
            );
            using Mat mainDist = CalibImageUtils.DeserializeVector(mainCamParam.DistCoeffsJson);
            using Mat secondaryDist = CalibImageUtils.DeserializeVector(
                secondaryCamParam.DistCoeffsJson
            );
            using Mat rectificationR1 = CalibImageUtils.DeserializeMatrix(
                stereoResult.RectificationR1Json,
                3,
                3
            );
            using Mat rectificationR2 = CalibImageUtils.DeserializeMatrix(
                stereoResult.RectificationR2Json,
                3,
                3
            );
            Size mapSize = new(stereoResult.RectifyMapWidth, stereoResult.RectifyMapHeight);

            Cv2.InitUndistortRectifyMap(
                mainMatrix,
                mainDist,
                rectificationR1,
                projectionP1,
                mapSize,
                MatType.CV_32FC1,
                map1x,
                map1y
            );
            Cv2.InitUndistortRectifyMap(
                secondaryMatrix,
                secondaryDist,
                rectificationR2,
                projectionP2,
                mapSize,
                MatType.CV_32FC1,
                map2x,
                map2y
            );

            await _blobContainer.SaveAsync(
                stereoResult.Map1XBlobKey,
                SerializeFloatMap(map1x),
                overrideExisting: true
            );
            await _blobContainer.SaveAsync(
                stereoResult.Map1YBlobKey,
                SerializeFloatMap(map1y),
                overrideExisting: true
            );
            await _blobContainer.SaveAsync(
                stereoResult.Map2XBlobKey,
                SerializeFloatMap(map2x),
                overrideExisting: true
            );
            await _blobContainer.SaveAsync(
                stereoResult.Map2YBlobKey,
                SerializeFloatMap(map2y),
                overrideExisting: true
            );
            _logger.LogWarning(
                "双目矫正映射 Blob 缺失，已根据标定参数自动重建并回写：ProjectId={ProjectId}",
                project.Id
            );
        }

        return new CalibrationData
        {
            ProjectionP1 = projectionP1.Clone(),
            ProjectionP2 = projectionP2.Clone(),
            BaselineMm = baselineMm,
            Map1x = map1x,
            Map1y = map1y,
            Map2x = map2x,
            Map2y = map2y,
            PatternCount = projParam?.PatternCount ?? 0,
            PeriodCount = projParam?.PeriodCount ?? 0,
            ProjectorWidth = projParam?.ResolutionWidth ?? 0,
            ProjectorHeight = projParam?.ResolutionHeight ?? 0,
        };
    }

    private static byte[] SerializeFloatMap(Mat map)
    {
        int byteCount = checked(map.Rows * map.Cols * sizeof(float));
        byte[] bytes = new byte[byteCount];
        System.Runtime.InteropServices.Marshal.Copy(map.Data, bytes, 0, byteCount);
        return bytes;
    }

    private async Task<ScanImages> LoadScanImagesAsync(
        CalibProject project,
        int totalFrames,
        CancellationToken cancellationToken)
    {
        ScanImages result = new();

        long maxRound = 1;
        for (long round = 1; round <= maxRound; round++)
        {
            for (int frame = 0; frame < totalFrames; frame++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (project.MainCameraDeviceId.HasValue)
                {
                    string mainKey = CalibScanAppService.BuildScanBlobKey(
                        project.Id,
                        project.MainCameraDeviceId.Value,
                        round,
                        frame,
                        CalibScanCameraRole.Main
                    );

                    if (await _blobContainer.ExistsAsync(mainKey))
                    {
                        byte[] bytes = await _blobContainer.GetAllBytesAsync(mainKey);
                        result.MainImages.Add(bytes);
                    }
                }

                if (project.SecondaryCameraDeviceId.HasValue)
                {
                    string secondaryKey = CalibScanAppService.BuildScanBlobKey(
                        project.Id,
                        project.SecondaryCameraDeviceId.Value,
                        round,
                        frame,
                        CalibScanCameraRole.Secondary
                    );

                    if (await _blobContainer.ExistsAsync(secondaryKey))
                    {
                        byte[] bytes = await _blobContainer.GetAllBytesAsync(secondaryKey);
                        result.SecondaryImages.Add(bytes);
                    }
                }
            }

            if (round == maxRound && project.MainCameraDeviceId.HasValue)
            {
                string textureKey = CalibScanAppService.BuildTextureBlobKey(
                    project.Id,
                    project.MainCameraDeviceId.Value,
                    round
                );
                if (await _blobContainer.ExistsAsync(textureKey))
                {
                    result.TextureImage = await _blobContainer.GetAllBytesAsync(textureKey);
                }
            }
        }

        return result;
    }

    private async Task ReportProgressAsync(
        Guid calibProjectId,
        int progress,
        string message,
        CancellationToken cancellationToken,
        params object[] args)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string formattedMessage = args.Length > 0 ? string.Format(message, args) : message;
        PointCloudStatusDto? dto = _stateStore.UpdateProgress(calibProjectId, progress, formattedMessage);
        if (dto != null)
        {
            await _notifier.NotifyStatusAsync(dto);
        }
    }

    private void LogStructuredLightDiagnostics(
        Guid projectId,
        long roundIndex,
        string cameraRole,
        GrayCodeDecodeResult result
    )
    {
        double validRate = result.Valid.Length == 0
            ? 0
            : result.ValidCount * 100d / result.Valid.Length;
        _logger.LogInformation(
            "Step7 条纹解码汇总：ProjectId={ProjectId}, Round={Round}, Camera={Camera}, Size={Width}x{Height}, Valid={Valid}/{Total} ({ValidRate:F2} %)",
            projectId,
            roundIndex,
            cameraRole,
            result.Width,
            result.Height,
            result.ValidCount,
            result.Valid.Length,
            validRate
        );

        foreach (GrayCodePairDiagnostics pair in result.PairDiagnostics)
        {
            double strongRate = pair.TotalPixels == 0
                ? 0
                : pair.StrongDifferenceCount * 100d / pair.TotalPixels;
            double positiveRate = pair.StrongDifferenceCount == 0
                ? 0
                : pair.PositiveDifferenceCount * 100d / pair.StrongDifferenceCount;
            _logger.LogInformation(
                "Step7 条纹互补对诊断：ProjectId={ProjectId}, Round={Round}, Camera={Camera}, Frames={OriginalFrame}/{InverseFrame}, Pattern={Pattern}, "
                + "Mean={OriginalMean:F2}/{InverseMean:F2}, MeanAbsDiff={MeanAbsDiff:F2}, StrongDiff={Strong}/{Total} ({StrongRate:F2} %), PositivePolarity={PositiveRate:F2} %",
                projectId,
                roundIndex,
                cameraRole,
                pair.FirstFrameIndex,
                pair.FirstFrameIndex + 1,
                pair.PatternLabel,
                pair.OriginalMean,
                pair.InverseMean,
                pair.MeanAbsoluteDifference,
                pair.StrongDifferenceCount,
                pair.TotalPixels,
                strongRate,
                positiveRate
            );
        }
    }

    /// <summary>
    /// 将完整深度矩阵压缩为二维质量拟合图。
    /// 红色表示该采样块没有有效深度；有效块按有效像素占比从黄色渐变到绿色。
    /// </summary>
    private static DepthQualityPreview BuildDepthQualityPreview(Mat depth)
    {
        const int maximumPreviewWidth = 640;
        int sampleStep = Math.Max(1, (int)Math.Ceiling(depth.Cols / (double)maximumPreviewWidth));
        int previewWidth = (depth.Cols + sampleStep - 1) / sampleStep;
        int previewHeight = (depth.Rows + sampleStep - 1) / sampleStep;
        int validPointCount = 0;
        double minimumDepth = double.PositiveInfinity;
        double maximumDepth = double.NegativeInfinity;

        using Mat preview = new(previewHeight, previewWidth, MatType.CV_8UC3);
        for (int previewY = 0; previewY < previewHeight; previewY++)
        {
            int sourceYStart = previewY * sampleStep;
            int sourceYEnd = Math.Min(sourceYStart + sampleStep, depth.Rows);
            for (int previewX = 0; previewX < previewWidth; previewX++)
            {
                int sourceXStart = previewX * sampleStep;
                int sourceXEnd = Math.Min(sourceXStart + sampleStep, depth.Cols);
                int blockValidCount = 0;
                int blockTotalCount = (sourceYEnd - sourceYStart) * (sourceXEnd - sourceXStart);

                for (int sourceY = sourceYStart; sourceY < sourceYEnd; sourceY++)
                {
                    for (int sourceX = sourceXStart; sourceX < sourceXEnd; sourceX++)
                    {
                        double value = depth.At<double>(sourceY, sourceX);
                        if (!double.IsFinite(value) || value <= 0)
                        {
                            continue;
                        }

                        blockValidCount++;
                        validPointCount++;
                        minimumDepth = Math.Min(minimumDepth, value);
                        maximumDepth = Math.Max(maximumDepth, value);
                    }
                }

                if (blockValidCount == 0)
                {
                    // RGB #ef4444（OpenCV 使用 BGR）
                    preview.Set(previewY, previewX, new Vec3b(68, 68, 239));
                    continue;
                }

                double score = blockValidCount / (double)blockTotalCount;
                // 低分黄色 #facc15 -> 高分绿色 #22c55e。
                byte red = (byte)Math.Round(250 + (34 - 250) * score);
                byte green = (byte)Math.Round(204 + (197 - 204) * score);
                byte blue = (byte)Math.Round(21 + (94 - 21) * score);
                preview.Set(previewY, previewX, new Vec3b(blue, green, red));
            }
        }

        Cv2.ImEncode(".png", preview, out byte[] pngBytes);
        return new DepthQualityPreview(
            pngBytes,
            validPointCount,
            checked(depth.Rows * depth.Cols),
            double.IsFinite(minimumDepth) ? minimumDepth : 0,
            double.IsFinite(maximumDepth) ? maximumDepth : 0
        );
    }

    private void LogStructuredLightMatchDiagnostics(
        Guid projectId,
        long roundIndex,
        int disparitySign,
        GrayCodeMatchDiagnostics diagnostics
    )
    {
        double codeHitRate = diagnostics.MainValidPixels == 0
            ? 0
            : diagnostics.CodeFoundPixels * 100d / diagnostics.MainValidPixels;
        double matchRate = diagnostics.MainValidPixels == 0
            ? 0
            : diagnostics.MatchedPixels * 100d / diagnostics.MainValidPixels;
        _logger.LogInformation(
            "Step7 条纹立体匹配诊断：ProjectId={ProjectId}, Round={Round}, Direction={Direction}, MainValid={MainValid}, "
            + "CodeFound={CodeFound} ({CodeHitRate:F2} %), CandidateCount={Candidates}, RejectedNonPositive={RejectedNonPositive}, "
            + "CandidateDisparityRange=[{CandidateMin:F2},{CandidateMax:F2}], MaxAllowed={MaxAllowed:F0}, "
            + "RejectedOverMax={RejectedOverMax}, AmbiguousPixels={AmbiguousPixels}, "
            + "Matched={Matched} ({MatchRate:F2} %), RowsWithSecondaryCodes={RowsWithCodes}",
            projectId,
            roundIndex,
            disparitySign > 0 ? "Positive" : "Negative",
            diagnostics.MainValidPixels,
            diagnostics.CodeFoundPixels,
            codeHitRate,
            diagnostics.CandidateCount,
            diagnostics.NonPositiveRejected,
            diagnostics.MinimumPositiveCandidate,
            diagnostics.MaximumPositiveCandidate,
            MaxStructuredLightDisparity,
            diagnostics.OverRangeRejected,
            diagnostics.AmbiguousPixelCount,
            diagnostics.MatchedPixels,
            matchRate,
            diagnostics.RowsWithSecondaryCodes
        );
    }

    private class CalibrationData
    {
        public Mat ProjectionP1 { get; set; } = new();
        public Mat ProjectionP2 { get; set; } = new();
        public double BaselineMm { get; set; }
        public Mat Map1x { get; set; } = new();
        public Mat Map1y { get; set; } = new();
        public Mat Map2x { get; set; } = new();
        public Mat Map2y { get; set; } = new();
        public int PatternCount { get; set; }
        public int PeriodCount { get; set; }
        public int ProjectorWidth { get; set; }
        public int ProjectorHeight { get; set; }
    }

    private class ScanImages
    {
        public List<byte[]> MainImages { get; } = new();
        public List<byte[]> SecondaryImages { get; } = new();
        public byte[]? TextureImage { get; set; }
    }

    private sealed record GrayCodeDecodeResult(
        int Width,
        int Height,
        int[] ProjectorX,
        int[] ProjectorY,
        byte[] Valid,
        int ValidCount,
        IReadOnlyList<GrayCodePairDiagnostics> PairDiagnostics
    );

    private sealed record GrayCodePairDiagnostics(
        int FirstFrameIndex,
        string PatternLabel,
        double OriginalMean,
        double InverseMean,
        double MeanAbsoluteDifference,
        int StrongDifferenceCount,
        int PositiveDifferenceCount,
        int TotalPixels
    );

    private sealed record GrayCodeMatchDiagnostics(
        long MainValidPixels,
        long CodeFoundPixels,
        long CandidateCount,
        long NonPositiveRejected,
        long OverRangeRejected,
        long AmbiguousPixelCount,
        long MatchedPixels,
        long RowsWithSecondaryCodes,
        double MinimumPositiveCandidate,
        double MaximumPositiveCandidate
    );

    private sealed record DepthQualityPreview(
        byte[] PngBytes,
        int ValidPointCount,
        int TotalPointCount,
        double MinimumDepthMm,
        double MaximumDepthMm
    );
}
