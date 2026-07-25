using AuroraStruct3D.Calibration.Dtos;
using AuroraStruct3D.Cameras;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System.Text;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.Calibration;

[Authorize]
public class CalibPointCloudAppService : AuroraStruct3DAppService, ICalibPointCloudAppService
{
    private readonly IRepository<CalibProject, Guid> _projectRepository;
    private readonly IRepository<CalibCameraParam, Guid> _cameraParamRepository;
    private readonly IRepository<CalibStereoResult, Guid> _stereoResultRepository;
    private readonly IRepository<CalibProjectorParam, Guid> _projectorParamRepository;
    private readonly IRepository<CameraDevice, Guid> _cameraDeviceRepository;
    private readonly CalibPointCloudStateStore _stateStore;
    private readonly ICalibPointCloudNotifier _notifier;
    private readonly IBlobContainer<CalibPhotoBlobContainer> _blobContainer;
    private readonly ILogger<CalibPointCloudAppService> _logger;

    public CalibPointCloudAppService(
        IRepository<CalibProject, Guid> projectRepository,
        IRepository<CalibCameraParam, Guid> cameraParamRepository,
        IRepository<CalibStereoResult, Guid> stereoResultRepository,
        IRepository<CalibProjectorParam, Guid> projectorParamRepository,
        IRepository<CameraDevice, Guid> cameraDeviceRepository,
        CalibPointCloudStateStore stateStore,
        ICalibPointCloudNotifier notifier,
        IBlobContainer<CalibPhotoBlobContainer> blobContainer,
        ILogger<CalibPointCloudAppService> logger
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
        int patternCount)
    {
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

            var scanImages = await LoadScanImagesForRoundAsync(project, roundIndex, patternCount);
            if (scanImages.MainImages.Count == 0 || scanImages.SecondaryImages.Count == 0)
            {
                _logger.LogWarning("Step7 增量点云生成：第 {Round} 轮扫描图像不完整，跳过", roundIndex);
                return;
            }

            var mainPhase = ComputePhaseForCamera(scanImages.MainImages, patternCount, calibrationData.PeriodCount, CancellationToken.None);
            var secondaryPhase = ComputePhaseForCamera(scanImages.SecondaryImages, patternCount, calibrationData.PeriodCount, CancellationToken.None);

            using Mat disparity = StereoReconstructionUtils.ComputeDisparity(mainPhase, secondaryPhase);
            using Mat depth = StereoReconstructionUtils.ComputeDepthFromDisparity(
                disparity,
                calibrationData.ProjectionP1,
                calibrationData.ProjectionP2,
                calibrationData.BaselineMm
            );

            using (Mat rectifiedMain = CalibImageUtils.LoadBgrMat(scanImages.MainImages[0]))
            using (Mat rectified = new())
            {
                Cv2.Remap(rectifiedMain, rectified, calibrationData.Map1x, calibrationData.Map1y, InterpolationFlags.Linear);

                var (pointCloud, colors) = StereoReconstructionUtils.GeneratePointCloud(
                    depth,
                    rectified,
                    calibrationData.ProjectionP1
                );

                byte[] plyBytes = StereoReconstructionUtils.WritePly(pointCloud, colors);

                int totalPointCount = _stateStore.GetTotalPointCount(calibProjectId) + pointCloud.Rows;
                _stateStore.AddIncrementalPointCloud(calibProjectId, plyBytes, pointCloud.Rows);

                await _notifier.NotifyIncrementalPointCloudAsync(
                    calibProjectId,
                    plyBytes,
                    pointCloud.Rows,
                    totalPointCount
                );

                _logger.LogInformation(
                    "Step7 增量点云生成完成：ProjectId={ProjectId}, Round={Round}, PointCount={PointCount}, TotalPointCount={TotalPointCount}",
                    calibProjectId,
                    roundIndex,
                    pointCloud.Rows,
                    totalPointCount
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Step7 增量点云生成失败：ProjectId={ProjectId}, Round={Round}", calibProjectId, roundIndex);
        }
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
        int patternCount)
    {
        ScanImages result = new();
        int totalFrames = patternCount * 2;

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

        List<string> headerLines = new();
        List<string> dataLines = new();
        int totalVertexCount = 0;

        foreach (byte[] chunk in plyChunks)
        {
            string content = Encoding.ASCII.GetString(chunk);
            string[] lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            bool isHeader = true;
            int vertexCount = 0;

            foreach (string line in lines)
            {
                string trimmed = line.Trim();

                if (isHeader)
                {
                    if (trimmed.StartsWith("element vertex"))
                    {
                        if (int.TryParse(trimmed.Split()[2], out int count))
                        {
                            vertexCount = count;
                        }
                    }
                    else if (trimmed == "end_header")
                    {
                        isHeader = false;
                        totalVertexCount += vertexCount;
                    }
                }
                else
                {
                    dataLines.Add(line);
                }
            }
        }

        StringBuilder merged = new();
        merged.AppendLine("ply");
        merged.AppendLine("format ascii 1.0");
        merged.AppendLine($"element vertex {totalVertexCount}");
        merged.AppendLine("property float x");
        merged.AppendLine("property float y");
        merged.AppendLine("property float z");
        merged.AppendLine("property uchar red");
        merged.AppendLine("property uchar green");
        merged.AppendLine("property uchar blue");
        merged.AppendLine("end_header");

        foreach (string line in dataLines)
        {
            merged.AppendLine(line);
        }

        return Encoding.ASCII.GetBytes(merged.ToString());
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

            var scanImages = await LoadScanImagesAsync(project, calibrationData.PatternCount, cancellationToken);
            if (scanImages.MainImages.Count == 0 || scanImages.SecondaryImages.Count == 0)
            {
                throw new UserFriendlyException("未找到扫描图像，请先执行在线扫描采集");
            }

            await ReportProgressAsync(project.Id, 25, "扫描图像加载完成，共 {MainCount} 张主相机图，{SecondaryCount} 张从相机图", cancellationToken,
                scanImages.MainImages.Count, scanImages.SecondaryImages.Count);

            await ReportProgressAsync(project.Id, 30, "正在进行相位解包...", cancellationToken);

            int patternCount = calibrationData.PatternCount;
            int periodCount = calibrationData.PeriodCount;

            var mainPhase = ComputePhaseForCamera(scanImages.MainImages, patternCount, periodCount, cancellationToken);
            var secondaryPhase = ComputePhaseForCamera(scanImages.SecondaryImages, patternCount, periodCount, cancellationToken);

            await ReportProgressAsync(project.Id, 50, "相位解包完成", cancellationToken);

            await ReportProgressAsync(project.Id, 55, "正在进行立体匹配...", cancellationToken);

            using Mat disparity = StereoReconstructionUtils.ComputeDisparity(
                mainPhase,
                secondaryPhase,
                minDisparity: 0,
                numDisparities: 64,
                blockSize: 15
            );

            await ReportProgressAsync(project.Id, 65, "立体匹配完成", cancellationToken);

            await ReportProgressAsync(project.Id, 70, "正在计算深度图...", cancellationToken);

            double baselineMm = calibrationData.BaselineMm;
            using Mat depth = StereoReconstructionUtils.ComputeDepthFromDisparity(
                disparity,
                calibrationData.ProjectionP1,
                calibrationData.ProjectionP2,
                baselineMm
            );

            await ReportProgressAsync(project.Id, 80, "深度图计算完成", cancellationToken);

            await ReportProgressAsync(project.Id, 85, "正在生成点云...", cancellationToken);

            using (Mat rectifiedMain = CalibImageUtils.LoadBgrMat(scanImages.MainImages[0]))
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
        CancellationToken cancellationToken)
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

        if (projParam == null || projParam.PatternCount <= 0)
        {
            return null;
        }

        using Mat projectionP1 = CalibImageUtils.DeserializeMatrix(stereoResult.ProjectionP1Json);
        using Mat projectionP2 = CalibImageUtils.DeserializeMatrix(stereoResult.ProjectionP2Json);

        using Mat baseline = StereoReconstructionUtils.ComputeBaselineFromStereoResult(projectionP1, projectionP2);
        double baselineMm = StereoReconstructionUtils.ComputeBaselineDistance(baseline);

        byte[] map1xBytes = await _blobContainer.GetAllBytesAsync(stereoResult.Map1XBlobKey);
        byte[] map1yBytes = await _blobContainer.GetAllBytesAsync(stereoResult.Map1YBlobKey);
        byte[] map2xBytes = await _blobContainer.GetAllBytesAsync(stereoResult.Map2XBlobKey);
        byte[] map2yBytes = await _blobContainer.GetAllBytesAsync(stereoResult.Map2YBlobKey);

        Mat map1x = new(stereoResult.RectifyMapHeight, stereoResult.RectifyMapWidth, MatType.CV_32FC1);
        Mat map1y = new(stereoResult.RectifyMapHeight, stereoResult.RectifyMapWidth, MatType.CV_32FC1);
        Mat map2x = new(stereoResult.RectifyMapHeight, stereoResult.RectifyMapWidth, MatType.CV_32FC1);
        Mat map2y = new(stereoResult.RectifyMapHeight, stereoResult.RectifyMapWidth, MatType.CV_32FC1);

        System.Runtime.InteropServices.Marshal.Copy(map1xBytes, 0, map1x.Data, map1xBytes.Length);
        System.Runtime.InteropServices.Marshal.Copy(map1yBytes, 0, map1y.Data, map1yBytes.Length);
        System.Runtime.InteropServices.Marshal.Copy(map2xBytes, 0, map2x.Data, map2xBytes.Length);
        System.Runtime.InteropServices.Marshal.Copy(map2yBytes, 0, map2y.Data, map2yBytes.Length);

        return new CalibrationData
        {
            ProjectionP1 = projectionP1.Clone(),
            ProjectionP2 = projectionP2.Clone(),
            BaselineMm = baselineMm,
            Map1x = map1x,
            Map1y = map1y,
            Map2x = map2x,
            Map2y = map2y,
            PatternCount = projParam.PatternCount,
            PeriodCount = projParam.PeriodCount,
            ProjectorWidth = projParam.ResolutionWidth,
            ProjectorHeight = projParam.ResolutionHeight,
        };
    }

    private async Task<ScanImages> LoadScanImagesAsync(
        CalibProject project,
        int patternCount,
        CancellationToken cancellationToken)
    {
        ScanImages result = new();

        long maxRound = 1;
        int totalFrames = patternCount * 2;

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
        }

        return result;
    }

    private Mat ComputePhaseForCamera(
        List<byte[]> images,
        int patternCount,
        int periodCount,
        CancellationToken cancellationToken)
    {
        int totalFrames = patternCount * 2;
        int halfFrames = patternCount;

        List<Mat> horizontalImages = new();
        List<Mat> verticalImages = new();

        for (int i = 0; i < images.Count && i < totalFrames; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using Mat bgr = CalibImageUtils.LoadBgrMat(images[i]);
            if (i < halfFrames)
            {
                horizontalImages.Add(bgr.Clone());
            }
            else
            {
                verticalImages.Add(bgr.Clone());
            }
        }

        using Mat absolutePhase = StructuredLightUtils.ComputeAbsolutePhase(
            horizontalImages,
            verticalImages,
            periodCount,
            periodCount
        );

        Mat[] channels = Cv2.Split(absolutePhase);
        try
        {
            return channels[0].Clone();
        }
        finally
        {
            foreach (Mat ch in channels) ch.Dispose();
        }
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
    }
}