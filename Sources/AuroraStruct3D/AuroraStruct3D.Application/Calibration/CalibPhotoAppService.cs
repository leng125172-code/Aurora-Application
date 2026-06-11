using System.Runtime.InteropServices;
using System.Text.Json;
using AuroraStruct3D.Calibration.Dtos;
using AuroraStruct3D.Cameras;
using AuroraStruct3D.Cameras.Dtos;
using AuroraStruct3D.Projectors;
using AuroraStruct3D.Projectors.Dtos;
using AuroraStruct3D.Tucam;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using SkiaSharp;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// 标定照片管理应用服务实现（Step 5）。
/// 负责：棋盘格参数配置、内参/外参拍照（含 OpenCV 角点检测）、照片查询/删除、内外参计算。
/// </summary>
[Authorize]
public class CalibPhotoAppService : AuroraStruct3DAppService, ICalibPhotoAppService
{
    private readonly IRepository<CalibProject, Guid> _projectRepo;
    private readonly IRepository<CalibCameraParam, Guid> _cameraParamRepo;
    private readonly IRepository<CalibPhotoRecord, Guid> _photoRepo;
    private readonly IRepository<CalibStereoResult, Guid> _stereoResultRepo;
    private readonly IBlobContainer<CalibPhotoBlobContainer> _blobContainer;
    private readonly IRepository<CameraDevice, Guid> _cameraDeviceRepository;
    private readonly ITucamCameraService _tucamService;
    private readonly IProjectorDeviceAppService _projectorService;
    private readonly ILogger<CalibPhotoAppService> _logger;

    /// <summary>构造注入</summary>
    public CalibPhotoAppService(
        IRepository<CalibProject, Guid> projectRepo,
        IRepository<CalibCameraParam, Guid> cameraParamRepo,
        IRepository<CalibPhotoRecord, Guid> photoRepo,
        IRepository<CalibStereoResult, Guid> stereoResultRepo,
        IBlobContainer<CalibPhotoBlobContainer> blobContainer,
        IRepository<CameraDevice, Guid> cameraDeviceRepository,
        ITucamCameraService tucamService,
        IProjectorDeviceAppService projectorService,
        ILogger<CalibPhotoAppService> logger
    )
    {
        _projectRepo = projectRepo;
        _cameraParamRepo = cameraParamRepo;
        _photoRepo = photoRepo;
        _stereoResultRepo = stereoResultRepo;
        _blobContainer = blobContainer;
        _cameraDeviceRepository = cameraDeviceRepository;
        _tucamService = tucamService;
        _projectorService = projectorService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<CalibBoardConfigDto> UpdateBoardConfigAsync(UpdateBoardConfigInput input)
    {
        CalibProject project = await _projectRepo.GetAsync(input.CalibProjectId);

        project.SetBoardConfig(
            input.PhysicalCornerRows,
            input.PhysicalCornerCols,
            input.PhysicalSquareSizeMm,
            input.ProjectedCornerRows,
            input.ProjectedCornerCols,
            input.ProjectedPixelSize
        );

        await _projectRepo.UpdateAsync(project);
        return ToBoardConfigDto(project);
    }

    /// <inheritdoc/>
    public async Task<CalibBoardConfigDto> GetBoardConfigAsync(Guid calibProjectId)
    {
        CalibProject project = await _projectRepo.GetAsync(calibProjectId);
        return ToBoardConfigDto(project);
    }

    /// <inheritdoc/>
    public async Task<CalibPhotoDto> TakeIntrinsicPhotoAsync(TakeIntrinsicPhotoInput input)
    {
        // 获取项目棋盘格参数
        CalibProject project = await _projectRepo.GetAsync(input.CalibProjectId);

        if (
            project.DeviceSeries == DeviceSeries.SingleLight
            && project.BoundProjectorDeviceId.HasValue
            && input.ProjectorDeviceId.HasValue
            && input.ProjectorDeviceId.Value != project.BoundProjectorDeviceId.Value
        )
        {
            throw new UserFriendlyException("当前项目绑定的投影机与拍照参数不一致，请刷新后重试");
        }

        // 如果传入了投影仪 ID，先关圆 LED，确保内参拍照不受投影光干扰
        if (input.ProjectorDeviceId.HasValue && project.DeviceSeries == DeviceSeries.SingleLight)
        {
            await _projectorService.LedOffAsync(input.ProjectorDeviceId.Value);
        }

        // 内参拍照：切换到软件触发模式，避免自由运行模式下相机持续输出干扰拍照
        byte[] jpegBytes = await GrabCalibFrameRawAsync(input.CameraDeviceId, targetTriggerMode: 2);

        // OpenCV 棋盘格角点检测
        (bool isValid, int cornerCount) = DetectChessboardCorners(
            jpegBytes,
            project.PhysicalCornerCols,
            project.PhysicalCornerRows
        );

        // 生成缩略图
        string? thumbBase64 = GenerateThumbnailBase64(jpegBytes);

        // 存 BLOB
        string blobKey = BuildBlobKey(
            input.CalibProjectId,
            input.CameraDeviceId,
            CalibPhotoType.Intrinsic
        );
        await _blobContainer.SaveAsync(blobKey, jpegBytes, overrideExisting: false);

        // 写数据库
        CalibPhotoRecord record = new(
            GuidGenerator.Create(),
            input.CalibProjectId,
            input.CameraDeviceId,
            CalibPhotoType.Intrinsic,
            blobKey,
            isValid,
            cornerCount,
            thumbBase64
        );
        await _photoRepo.InsertAsync(record);

        return ToPhotoDto(record);
    }

    /// <inheritdoc/>
    public async Task<CalibPhotoDto> TakeExtrinsicPhotoAsync(TakeExtrinsicPhotoInput input)
    {
        CalibProject project = await _projectRepo.GetAsync(input.CalibProjectId);

        if (project.DeviceSeries == DeviceSeries.NoLight)
        {
            throw new UserFriendlyException("无光系列不支持外参拍照");
        }

        if (!project.BoundProjectorDeviceId.HasValue)
        {
            throw new UserFriendlyException("请先在项目管理页绑定主结构光机后再执行外参拍照");
        }

        if (
            project.DeviceType == CalibDeviceType.TwoCamera1Light
            && project.MainCameraDeviceId.HasValue
            && input.CameraDeviceId != project.MainCameraDeviceId.Value
        )
        {
            throw new UserFriendlyException(
                "2目1光模式下，投影外参拍照仅支持主相机；右相机与投影仪关系由双目外参自动推导。"
            );
        }

        Guid projectorDeviceId = project.BoundProjectorDeviceId.Value;

        // 1. 开灯
        await _projectorService.LedOnAsync(projectorDeviceId);

        // 2. 设置显示模式为棋盘格
        await _projectorService.SetDisplayModeAsync(
            new SetProjectorDisplayModeDto
            {
                ProjectorDeviceId = projectorDeviceId,
                Mode = ProjectorDisplayMode.Checkerboard,
            }
        );

        // 3. 设置棋盘格像素尺寸（使用项目配置的 ProjectedPixelSize）
        await _projectorService.SetCheckerboardPixelSizeAsync(
            new SetProjectorCheckerboardDto
            {
                ProjectorDeviceId = projectorDeviceId,
                PixelSize = project.ProjectedPixelSize > 0 ? project.ProjectedPixelSize : 30,
            }
        );

        // 4. 短暂延迟，等待投影仪稳定（约 200ms）
        await Task.Delay(200);

        // 5. 外参拍照：切换到软件触发模式（TriggerSoftwarePulse），投影仪已稳定输出棋盘格
        byte[] jpegBytes;
        try
        {
            jpegBytes = await GrabCalibFrameRawAsync(input.CameraDeviceId, targetTriggerMode: 2);
        }
        finally
        {
            // 6. 关灯（拍完立即关，无论拍照是否成功）
            await _projectorService.LedOffAsync(projectorDeviceId);
        }

        // 外参照片检测：使用投影棋盘格内角点配置
        (bool isValid, int cornerCount) = DetectChessboardCorners(
            jpegBytes,
            project.ProjectedCornerCols,
            project.ProjectedCornerRows
        );

        string? thumbBase64 = GenerateThumbnailBase64(jpegBytes);

        string blobKey = BuildBlobKey(
            input.CalibProjectId,
            input.CameraDeviceId,
            CalibPhotoType.Extrinsic
        );
        await _blobContainer.SaveAsync(blobKey, jpegBytes, overrideExisting: false);

        CalibPhotoRecord record = new(
            GuidGenerator.Create(),
            input.CalibProjectId,
            input.CameraDeviceId,
            CalibPhotoType.Extrinsic,
            blobKey,
            isValid,
            cornerCount,
            thumbBase64
        );
        await _photoRepo.InsertAsync(record);

        return ToPhotoDto(record);
    }

    /// <inheritdoc/>
    public async Task<CalibStereoPairPhotoDto> TakeStereoExtrinsicPairPhotoAsync(
        TakeStereoExtrinsicPairPhotoInput input
    )
    {
        CalibProject project = await _projectRepo.GetAsync(input.CalibProjectId);

        if (!project.MainCameraDeviceId.HasValue || !project.SecondaryCameraDeviceId.HasValue)
        {
            throw new UserFriendlyException("当前项目未绑定主/从相机，无法进行双目联合外参拍照");
        }

        Guid mainCameraId = project.MainCameraDeviceId.Value;
        Guid secondaryCameraId = project.SecondaryCameraDeviceId.Value;

        // 若项目绑定了投影仪，先关灯确保不受投影光干扰
        if (project.BoundProjectorDeviceId.HasValue)
        {
            await _projectorService.LedOffAsync(project.BoundProjectorDeviceId.Value);
        }

        // 主相机先拍，从相机再拍（_capStartActiveLock 保证顶序执行）
        // 两台均切换到软件触发模式进行标定拍照
        byte[] mainBytes = await GrabCalibFrameRawAsync(mainCameraId, targetTriggerMode: 2);
        byte[] secondaryBytes = await GrabCalibFrameRawAsync(
            secondaryCameraId,
            targetTriggerMode: 2
        );

        (bool mainValid, int mainCornerCount) = DetectChessboardCorners(
            mainBytes,
            project.PhysicalCornerCols,
            project.PhysicalCornerRows
        );
        (bool secondaryValid, int secondaryCornerCount) = DetectChessboardCorners(
            secondaryBytes,
            project.PhysicalCornerCols,
            project.PhysicalCornerRows
        );

        Guid pairGroupId = GuidGenerator.Create();

        string mainBlobKey = BuildBlobKey(
            input.CalibProjectId,
            mainCameraId,
            CalibPhotoType.StereoExtrinsicPair
        );
        string secondaryBlobKey = BuildBlobKey(
            input.CalibProjectId,
            secondaryCameraId,
            CalibPhotoType.StereoExtrinsicPair
        );

        await _blobContainer.SaveAsync(mainBlobKey, mainBytes, overrideExisting: false);
        await _blobContainer.SaveAsync(secondaryBlobKey, secondaryBytes, overrideExisting: false);

        CalibPhotoRecord mainRecord = new(
            GuidGenerator.Create(),
            input.CalibProjectId,
            mainCameraId,
            CalibPhotoType.StereoExtrinsicPair,
            mainBlobKey,
            mainValid,
            mainCornerCount,
            GenerateThumbnailBase64(mainBytes),
            pairGroupId,
            StereoPhotoRole.Main
        );

        CalibPhotoRecord secondaryRecord = new(
            GuidGenerator.Create(),
            input.CalibProjectId,
            secondaryCameraId,
            CalibPhotoType.StereoExtrinsicPair,
            secondaryBlobKey,
            secondaryValid,
            secondaryCornerCount,
            GenerateThumbnailBase64(secondaryBytes),
            pairGroupId,
            StereoPhotoRole.Secondary
        );

        await _photoRepo.InsertAsync(mainRecord);
        await _photoRepo.InsertAsync(secondaryRecord);

        return new CalibStereoPairPhotoDto
        {
            PairGroupId = pairGroupId,
            MainPhoto = ToPhotoDto(mainRecord),
            SecondaryPhoto = ToPhotoDto(secondaryRecord),
        };
    }

    /// <inheritdoc/>
    public async Task<List<CalibPhotoDto>> GetPhotoListAsync(
        Guid calibProjectId,
        Guid cameraDeviceId,
        CalibPhotoType? photoType = null
    )
    {
        IQueryable<CalibPhotoRecord> query = await _photoRepo.GetQueryableAsync();
        query = query.Where(x =>
            x.CalibProjectId == calibProjectId && x.CameraDeviceId == cameraDeviceId
        );
        if (photoType.HasValue)
        {
            query = query.Where(x => x.PhotoType == photoType.Value);
        }
        query = query.OrderBy(x => x.CapturedAt);

        List<CalibPhotoRecord> items = await AsyncExecuter.ToListAsync(query);
        return items.Select(ToPhotoDto).ToList();
    }

    /// <inheritdoc/>
    /// <inheritdoc/>
    public async Task DeleteAsync(Guid id)
    {
        CalibPhotoRecord record = await _photoRepo.GetAsync(id);

        // 删除 BLOB 文件（忽略不存在的情况）
        try
        {
            await _blobContainer.DeleteAsync(record.BlobKey);
        }
        catch
        {
            // BLOB 不存在时忽略
        }

        await _photoRepo.DeleteAsync(id);
    }

    /// <inheritdoc/>
    public async Task<int> DeleteInvalidPhotosAsync(Guid calibProjectId, Guid cameraDeviceId)
    {
        IQueryable<CalibPhotoRecord> query = await _photoRepo.GetQueryableAsync();
        List<CalibPhotoRecord> invalidPhotos = await AsyncExecuter.ToListAsync(
            query.Where(x =>
                x.CalibProjectId == calibProjectId
                && x.CameraDeviceId == cameraDeviceId
                && !x.IsValid
            )
        );

        if (invalidPhotos.Count == 0)
            return 0;

        foreach (CalibPhotoRecord record in invalidPhotos)
        {
            try
            {
                await _blobContainer.DeleteAsync(record.BlobKey);
            }
            catch
            {
                // BLOB 不存在时忽略
            }
        }

        await _photoRepo.DeleteManyAsync(invalidPhotos.Select(x => x.Id));

        _logger.LogInformation(
            "删除无效照片: 项目 {ProjectId} 相机 {CameraId} 共删除 {Count} 张",
            calibProjectId,
            cameraDeviceId,
            invalidPhotos.Count
        );

        return invalidPhotos.Count;
    }

    /// <inheritdoc/>
    public async Task<CalibComputeResultDto> ComputeCalibrationAsync(
        Guid calibProjectId,
        Guid cameraDeviceId
    )
    {
        System.Diagnostics.Stopwatch totalSw = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation(
            "[单目标定] 开始计算 — 项目 {ProjectId} 相机 {CameraId}",
            calibProjectId,
            cameraDeviceId
        );

        CalibProject project = await _projectRepo.GetAsync(calibProjectId);

        if (
            project.PhysicalCornerRows < 2
            || project.PhysicalCornerCols < 2
            || project.PhysicalSquareSizeMm <= 0
        )
        {
            throw new UserFriendlyException("请先配置棋盘格参数（内角点行数、列数、方格边长）");
        }

        // 查询有效内参照片
        IQueryable<CalibPhotoRecord> query = await _photoRepo.GetQueryableAsync();
        List<CalibPhotoRecord> intrinsicPhotos = await AsyncExecuter.ToListAsync(
            query
                .Where(x =>
                    x.CalibProjectId == calibProjectId
                    && x.CameraDeviceId == cameraDeviceId
                    && x.PhotoType == CalibPhotoType.Intrinsic
                    && x.IsValid
                )
                .OrderBy(x => x.CapturedAt)
        );

        _logger.LogInformation(
            "[单目标定] 有效内参照片 {Count} 张，棋盘格 {Cols}x{Rows} 方格 {Size}mm",
            intrinsicPhotos.Count,
            project.PhysicalCornerCols,
            project.PhysicalCornerRows,
            project.PhysicalSquareSizeMm
        );

        if (intrinsicPhotos.Count < CalibConsts.MinValidPhotoCount)
        {
            throw new UserFriendlyException(
                $"内参有效照片不足 {CalibConsts.MinValidPhotoCount} 张（当前 {intrinsicPhotos.Count} 张），无法计算内参"
            );
        }

        // 查询有效外参照片（无光系列不做外参计算）
        List<CalibPhotoRecord> extrinsicPhotos = [];
        if (project.DeviceSeries == DeviceSeries.SingleLight)
        {
            extrinsicPhotos = await AsyncExecuter.ToListAsync(
                query
                    .Where(x =>
                        x.CalibProjectId == calibProjectId
                        && x.CameraDeviceId == cameraDeviceId
                        && x.PhotoType == CalibPhotoType.Extrinsic
                        && x.IsValid
                    )
                    .OrderBy(x => x.CapturedAt)
            );
        }

        // ── 计算内参 ─────────────────────────────────────────────────────────────
        int cornerCols = project.PhysicalCornerCols;
        int cornerRows = project.PhysicalCornerRows;
        float squareSizeMm = (float)project.PhysicalSquareSizeMm;
        Size patternSize = new(cornerCols, cornerRows);

        // 构建世界坐标（棋盘格平面，Z=0）
        Point3f[] worldCorners = BuildWorldCorners(cornerCols, cornerRows, squareSizeMm);

        // OpenCvSharp4 CalibrateCamera 需要 IEnumerable<Mat>，将 Point 数组转换为 Mat
        List<Mat> objectMats = [];
        List<Mat> imageMats = [];
        Size imageSize = default;

        System.Diagnostics.Stopwatch phaseSw = new();
        for (int _idx = 0; _idx < intrinsicPhotos.Count; _idx++)
        {
            CalibPhotoRecord photo = intrinsicPhotos[_idx];
            phaseSw.Restart();
            byte[] bytes = await _blobContainer.GetAllBytesAsync(photo.BlobKey);
            long blobMs = phaseSw.ElapsedMilliseconds;

            // 直接加载为灰度（跳过 BGR→Gray 转换开销），计算路径不需要彩色数据
            phaseSw.Restart();
            using Mat mat = LoadGrayMat(bytes);
            long loadMs = phaseSw.ElapsedMilliseconds;

            if (mat.Empty())
            {
                _logger.LogWarning(
                    "[单目标定] [{Idx}/{Total}] 图像解码失败，跳过",
                    _idx + 1,
                    intrinsicPhotos.Count
                );
                continue;
            }

            if (imageSize == default)
            {
                imageSize = new Size(mat.Cols, mat.Rows);
                _logger.LogInformation("[单目标定] 图像分辨率 {W}x{H}", mat.Cols, mat.Rows);
            }

            phaseSw.Restart();
            // 使用半分辨率加速版：在 50% 缩放图检测，还原至全分辨率精化
            Point2f[]? corners = FindCornersSubpixGray(mat, patternSize);
            long cornerMs = phaseSw.ElapsedMilliseconds;

            _logger.LogInformation(
                "[单目标定] [{Idx}/{Total}] blob={BlobMs}ms 解码={LoadMs}ms 角点={CornerMs}ms {Result}",
                _idx + 1,
                intrinsicPhotos.Count,
                blobMs,
                loadMs,
                cornerMs,
                corners != null ? $"成功({corners.Length}角点)" : "未找到角点"
            );

            if (corners == null)
                continue;

            objectMats.Add(Mat.FromArray(worldCorners));
            imageMats.Add(Mat.FromArray(corners));
        }

        if (objectMats.Count < CalibConsts.MinValidPhotoCount)
        {
            // 释放已创建的 Mat
            foreach (Mat m in objectMats)
                m.Dispose();
            foreach (Mat m in imageMats)
                m.Dispose();
            throw new UserFriendlyException("从 BLOB 重新加载后有效内参照片不足，请重新拍摄");
        }

        // calibrateCamera
        using Mat cameraMatrix = new();
        using Mat distCoeffs = new();

        _logger.LogInformation(
            "[单目标定] 开始 CalibrateCamera — {UsedCount} 张照片，分辨率 {W}x{H}",
            objectMats.Count,
            imageSize.Width,
            imageSize.Height
        );
        phaseSw.Restart();

        double reprojError;
        Mat[] rvecArray;
        Mat[] tvecArray;
        try
        {
            reprojError = Cv2.CalibrateCamera(
                objectMats,
                imageMats,
                imageSize,
                cameraMatrix,
                distCoeffs,
                out rvecArray,
                out tvecArray,
                CalibrationFlags.None
            );
        }
        finally
        {
            foreach (Mat m in objectMats)
                m.Dispose();
            foreach (Mat m in imageMats)
                m.Dispose();
        }

        _logger.LogInformation(
            "[单目标定] CalibrateCamera 耗时 {Ms}ms，重投影误差 {Error:F4} px",
            phaseSw.ElapsedMilliseconds,
            reprojError
        );

        // 序列化内参
        string intrinsicJson = SerializeMatToJson(cameraMatrix);
        string distJson = SerializeVecToJson(distCoeffs);

        // ── 计算外参（使用第一张有效外参照片）─────────────────────────────────────
        string? rvecJson = null;
        string? tvecJson = null;
        double? projectorReprojectionError = null;

        bool shouldComputeProjectorExtrinsic =
            project.DeviceSeries == DeviceSeries.SingleLight
            && (
                project.DeviceType != CalibDeviceType.TwoCamera1Light
                || !project.MainCameraDeviceId.HasValue
                || project.MainCameraDeviceId.Value == cameraDeviceId
            );

        if (
            shouldComputeProjectorExtrinsic
            && extrinsicPhotos.Count < CalibConsts.MinProjectorExtrinsicPhotoCount
        )
        {
            throw new UserFriendlyException(
                $"投影外参有效照片不足 {CalibConsts.MinProjectorExtrinsicPhotoCount} 张（当前 {extrinsicPhotos.Count} 张），无法计算外参"
            );
        }

        if (shouldComputeProjectorExtrinsic && extrinsicPhotos.Count > 0)
        {
            _logger.LogInformation(
                "[单目标定] 开始 FindBestExtrinsic — {Count} 张外参照片",
                extrinsicPhotos.Count
            );
            phaseSw.Restart();
            CalibPhotoRecord? bestExtrinsic = await FindBestExtrinsicAsync(
                extrinsicPhotos,
                cameraMatrix,
                distCoeffs,
                patternSize,
                worldCorners
            );
            _logger.LogInformation(
                "[单目标定] FindBestExtrinsic 耗时 {Ms}ms",
                phaseSw.ElapsedMilliseconds
            );

            if (bestExtrinsic != null)
            {
                byte[] exBytes = await _blobContainer.GetAllBytesAsync(bestExtrinsic.BlobKey);
                using Mat exMat = LoadGrayMat(exBytes);
                Point2f[]? exCorners = FindCornersSubpixGray(exMat, patternSize);
                if (exCorners != null)
                {
                    using Mat rvec = new();
                    using Mat tvec = new();
                    // SolvePnP 需要 InputArray，使用 InputArray.Create() 包装数组
                    Cv2.SolvePnP(
                        InputArray.Create(worldCorners),
                        InputArray.Create(exCorners),
                        cameraMatrix,
                        distCoeffs,
                        rvec,
                        tvec
                    );

                    using Mat projectedMat = new();
                    Cv2.ProjectPoints(
                        InputArray.Create(worldCorners),
                        rvec,
                        tvec,
                        cameraMatrix,
                        distCoeffs,
                        projectedMat
                    );
                    projectedMat.GetArray(out Point2f[] projected);

                    double err = 0;
                    for (int i = 0; i < exCorners.Length; i++)
                    {
                        double dx = exCorners[i].X - projected[i].X;
                        double dy = exCorners[i].Y - projected[i].Y;
                        err += Math.Sqrt(dx * dx + dy * dy);
                    }
                    projectorReprojectionError = err / exCorners.Length;

                    rvecJson = SerializeVecToJson(rvec);
                    tvecJson = SerializeVecToJson(tvec);
                    _logger.LogInformation(
                        "[单目标定] SolvePnP 外参计算完成，投影误差 {Err:F4} px",
                        projectorReprojectionError
                    );
                }
            }
        }

        _logger.LogInformation(
            "[单目标定] 全流程完成，总耗时 {TotalMs}ms，内参误差 {InErr:F4} px",
            totalSw.ElapsedMilliseconds,
            reprojError
        );

        if (reprojError > CalibConsts.MaxSingleCameraReprojectionError)
        {
            throw new UserFriendlyException(
                $"相机内参重投影误差为 {reprojError:F4} px，超过阈值 {CalibConsts.MaxSingleCameraReprojectionError:F2} px，请补拍后重算"
            );
        }

        if (
            shouldComputeProjectorExtrinsic
            && projectorReprojectionError.HasValue
            && projectorReprojectionError.Value > CalibConsts.MaxSingleCameraReprojectionError
        )
        {
            throw new UserFriendlyException(
                $"投影外参重投影误差为 {projectorReprojectionError.Value:F4} px，超过阈值 {CalibConsts.MaxSingleCameraReprojectionError:F2} px，请补拍后重算"
            );
        }

        // 释放 rvec/tvec 数组
        foreach (Mat m in rvecArray)
            m.Dispose();
        foreach (Mat m in tvecArray)
            m.Dispose();

        // ── 持久化结果到 CalibCameraParam ────────────────────────────────────────
        IQueryable<CalibCameraParam> paramQuery = await _cameraParamRepo.GetQueryableAsync();
        CalibCameraParam? camParam = await AsyncExecuter.FirstOrDefaultAsync(
            paramQuery.Where(x =>
                x.CalibProjectId == calibProjectId && x.CameraDeviceId == cameraDeviceId
            )
        );

        if (camParam != null)
        {
            camParam.SetCalibResult(
                intrinsicJson,
                distJson,
                reprojError,
                rvecJson,
                tvecJson,
                projectorReprojectionError
            );
            await _cameraParamRepo.UpdateAsync(camParam);
        }

        return new CalibComputeResultDto
        {
            IntrinsicMatrixJson = intrinsicJson,
            DistCoeffsJson = distJson,
            ReprojectionError = reprojError,
            ProjectorReprojectionError = projectorReprojectionError,
            ExtrinsicRvecJson = rvecJson,
            ExtrinsicTvecJson = tvecJson,
        };
    }

    /// <inheritdoc/>
    public async Task<CalibCameraStatusDto> GetCameraStatusAsync(
        Guid calibProjectId,
        Guid cameraDeviceId
    )
    {
        IQueryable<CalibPhotoRecord> query = await _photoRepo.GetQueryableAsync();
        List<CalibPhotoRecord> all = await AsyncExecuter.ToListAsync(
            query.Where(x =>
                x.CalibProjectId == calibProjectId && x.CameraDeviceId == cameraDeviceId
            )
        );

        int intrinsicTotal = all.Count(x => x.PhotoType == CalibPhotoType.Intrinsic);
        int intrinsicValid = all.Count(x => x.PhotoType == CalibPhotoType.Intrinsic && x.IsValid);
        int extrinsicTotal = all.Count(x => x.PhotoType == CalibPhotoType.Extrinsic);
        int extrinsicValid = all.Count(x => x.PhotoType == CalibPhotoType.Extrinsic && x.IsValid);

        // 读取最新标定结果
        IQueryable<CalibCameraParam> paramQuery = await _cameraParamRepo.GetQueryableAsync();
        CalibCameraParam? camParam = await AsyncExecuter.FirstOrDefaultAsync(
            paramQuery.Where(x =>
                x.CalibProjectId == calibProjectId && x.CameraDeviceId == cameraDeviceId
            )
        );

        CalibComputeResultDto? latestResult = null;
        if (camParam?.IntrinsicMatrixJson != null)
        {
            latestResult = new CalibComputeResultDto
            {
                IntrinsicMatrixJson = camParam.IntrinsicMatrixJson,
                DistCoeffsJson = camParam.DistCoeffsJson ?? string.Empty,
                ReprojectionError = camParam.ReprojectionError ?? 0,
                ProjectorReprojectionError = camParam.ProjectorReprojectionError,
                ExtrinsicRvecJson = camParam.ExtrinsicRvecJson,
                ExtrinsicTvecJson = camParam.ExtrinsicTvecJson,
            };
        }

        return new CalibCameraStatusDto
        {
            CameraDeviceId = cameraDeviceId,
            IntrinsicTotal = intrinsicTotal,
            IntrinsicValid = intrinsicValid,
            ExtrinsicTotal = extrinsicTotal,
            ExtrinsicValid = extrinsicValid,
            LatestResult = latestResult,
        };
    }

    /// <inheritdoc/>
    public async Task<CalibStereoComputeResultDto> ComputeStereoCalibrationAsync(
        Guid calibProjectId
    )
    {
        System.Diagnostics.Stopwatch totalSwS = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("[双目标定] 开始计算 — 项目 {ProjectId}", calibProjectId);

        CalibProject project = await _projectRepo.GetAsync(calibProjectId);

        if (!project.MainCameraDeviceId.HasValue || !project.SecondaryCameraDeviceId.HasValue)
        {
            throw new UserFriendlyException("当前项目未绑定主/从相机，无法计算双目联合外参");
        }

        Guid mainCameraId = project.MainCameraDeviceId.Value;
        Guid secondaryCameraId = project.SecondaryCameraDeviceId.Value;

        IQueryable<CalibCameraParam> paramQuery = await _cameraParamRepo.GetQueryableAsync();
        CalibCameraParam? mainParam = await AsyncExecuter.FirstOrDefaultAsync(
            paramQuery.Where(x =>
                x.CalibProjectId == calibProjectId && x.CameraDeviceId == mainCameraId
            )
        );
        CalibCameraParam? secondaryParam = await AsyncExecuter.FirstOrDefaultAsync(
            paramQuery.Where(x =>
                x.CalibProjectId == calibProjectId && x.CameraDeviceId == secondaryCameraId
            )
        );

        if (
            string.IsNullOrWhiteSpace(mainParam?.IntrinsicMatrixJson)
            || string.IsNullOrWhiteSpace(mainParam?.DistCoeffsJson)
            || string.IsNullOrWhiteSpace(secondaryParam?.IntrinsicMatrixJson)
            || string.IsNullOrWhiteSpace(secondaryParam?.DistCoeffsJson)
        )
        {
            throw new UserFriendlyException("请先完成主/从相机内参计算，再执行双目联合计算");
        }

        IQueryable<CalibPhotoRecord> photoQuery = await _photoRepo.GetQueryableAsync();
        List<CalibPhotoRecord> pairPhotos = await AsyncExecuter.ToListAsync(
            photoQuery
                .Where(x =>
                    x.CalibProjectId == calibProjectId
                    && x.PhotoType == CalibPhotoType.StereoExtrinsicPair
                    && x.PairGroupId != null
                    && x.IsValid
                )
                .OrderBy(x => x.CapturedAt)
        );

        List<(CalibPhotoRecord Main, CalibPhotoRecord Secondary)> validPairs = pairPhotos
            .GroupBy(x => x.PairGroupId!.Value)
            .Select(group =>
            {
                CalibPhotoRecord? main = group.FirstOrDefault(x =>
                    x.CameraDeviceId == mainCameraId && x.StereoRole == StereoPhotoRole.Main
                );
                CalibPhotoRecord? secondary = group.FirstOrDefault(x =>
                    x.CameraDeviceId == secondaryCameraId
                    && x.StereoRole == StereoPhotoRole.Secondary
                );
                return (Main: main, Secondary: secondary);
            })
            .Where(x => x.Main != null && x.Secondary != null)
            .Select(x => (x.Main!, x.Secondary!))
            .ToList();

        if (validPairs.Count < CalibConsts.MinValidPhotoCount)
        {
            throw new UserFriendlyException(
                $"双目成对有效照片不足 {CalibConsts.MinValidPhotoCount} 组（当前 {validPairs.Count} 组），无法计算双目联合外参"
            );
        }

        Size patternSize = new(project.PhysicalCornerCols, project.PhysicalCornerRows);
        float squareSizeMm = (float)project.PhysicalSquareSizeMm;
        Point3f[] worldCorners = BuildWorldCorners(
            project.PhysicalCornerCols,
            project.PhysicalCornerRows,
            squareSizeMm
        );

        List<Mat> objectPoints = [];
        List<Mat> imagePointsMain = [];
        List<Mat> imagePointsSecondary = [];
        Size imageSize = default;

        System.Diagnostics.Stopwatch phaseSwS = new();
        _logger.LogInformation(
            "[双目标定] 有效样本组数 {Count}，开始加载与角点检测",
            validPairs.Count
        );
        for (int _si = 0; _si < validPairs.Count; _si++)
        {
            (CalibPhotoRecord mainPhoto, CalibPhotoRecord secondaryPhoto) = validPairs[_si];
            phaseSwS.Restart();
            byte[] mainBytes = await _blobContainer.GetAllBytesAsync(mainPhoto.BlobKey);
            byte[] secondaryBytes = await _blobContainer.GetAllBytesAsync(secondaryPhoto.BlobKey);
            long blobMs = phaseSwS.ElapsedMilliseconds;

            // 直接加载为灰度（计算路径不需要彩色数据）
            phaseSwS.Restart();
            using Mat mainMat = LoadGrayMat(mainBytes);
            using Mat secondaryMat = LoadGrayMat(secondaryBytes);
            long loadMs = phaseSwS.ElapsedMilliseconds;

            if (mainMat.Empty() || secondaryMat.Empty())
            {
                _logger.LogWarning(
                    "[双目标定] [{Idx}/{Total}] 图像解码失败，跳过",
                    _si + 1,
                    validPairs.Count
                );
                continue;
            }

            if (imageSize == default)
            {
                imageSize = new Size(mainMat.Cols, mainMat.Rows);
                _logger.LogInformation("[双目标定] 图像分辨率 {W}x{H}", mainMat.Cols, mainMat.Rows);
            }

            phaseSwS.Restart();
            // 使用半分辨率加速版：50% 缩放图检测，坐标还原至全分辨率精化
            Point2f[]? mainCorners = FindCornersSubpixGray(mainMat, patternSize);
            Point2f[]? secondaryCorners = FindCornersSubpixGray(secondaryMat, patternSize);
            long cornerMs = phaseSwS.ElapsedMilliseconds;

            _logger.LogInformation(
                "[双目标定] [{Idx}/{Total}] blob={BlobMs}ms 解码={LoadMs}ms 角点={CornerMs}ms 主={MainResult} 从={SecResult}",
                _si + 1,
                validPairs.Count,
                blobMs,
                loadMs,
                cornerMs,
                mainCorners != null ? $"OK({mainCorners.Length})" : "失败",
                secondaryCorners != null ? $"OK({secondaryCorners.Length})" : "失败"
            );

            if (mainCorners == null || secondaryCorners == null)
            {
                continue;
            }

            objectPoints.Add(Mat.FromArray(worldCorners));
            imagePointsMain.Add(Mat.FromArray(mainCorners));
            imagePointsSecondary.Add(Mat.FromArray(secondaryCorners));
        }

        if (objectPoints.Count < CalibConsts.MinValidPhotoCount)
        {
            foreach (Mat m in objectPoints)
                m.Dispose();
            foreach (Mat m in imagePointsMain)
                m.Dispose();
            foreach (Mat m in imagePointsSecondary)
                m.Dispose();

            throw new UserFriendlyException("双目成对样本重检后不足，请补充拍照后重试");
        }

        using Mat mainCameraMatrix = DeserializeMatrix(mainParam.IntrinsicMatrixJson!);
        using Mat mainDistCoeffs = DeserializeVector(mainParam.DistCoeffsJson!);
        using Mat secondaryCameraMatrix = DeserializeMatrix(secondaryParam.IntrinsicMatrixJson!);
        using Mat secondaryDistCoeffs = DeserializeVector(secondaryParam.DistCoeffsJson!);

        using Mat r = new();
        using Mat t = new();
        using Mat e = new();
        using Mat f = new();
        double stereoError;

        _logger.LogInformation(
            "[双目标定] 开始 StereoCalibrate — {Count} 组样本，分辨率 {W}x{H}",
            objectPoints.Count,
            imageSize.Width,
            imageSize.Height
        );
        phaseSwS.Restart();

        try
        {
            stereoError = Cv2.StereoCalibrate(
                objectPoints,
                imagePointsMain,
                imagePointsSecondary,
                mainCameraMatrix,
                mainDistCoeffs,
                secondaryCameraMatrix,
                secondaryDistCoeffs,
                imageSize,
                r,
                t,
                e,
                f,
                CalibrationFlags.FixIntrinsic,
                new TermCriteria(CriteriaTypes.Eps | CriteriaTypes.MaxIter, 100, 1e-5)
            );
        }
        finally
        {
            foreach (Mat m in objectPoints)
                m.Dispose();
            foreach (Mat m in imagePointsMain)
                m.Dispose();
            foreach (Mat m in imagePointsSecondary)
                m.Dispose();
        }

        _logger.LogInformation(
            "[双目标定] StereoCalibrate 耗时 {Ms}ms，重投影误差 {Error:F4} px",
            phaseSwS.ElapsedMilliseconds,
            stereoError
        );

        using Mat r1 = new();
        using Mat r2 = new();
        using Mat p1 = new();
        using Mat p2 = new();
        using Mat q = new();
        Cv2.StereoRectify(
            mainCameraMatrix,
            mainDistCoeffs,
            secondaryCameraMatrix,
            secondaryDistCoeffs,
            imageSize,
            r,
            t,
            r1,
            r2,
            p1,
            p2,
            q
        );

        if (stereoError > CalibConsts.MaxStereoReprojectionError)
        {
            throw new UserFriendlyException(
                $"双目重投影误差为 {stereoError:F4} px，超过阈值 {CalibConsts.MaxStereoReprojectionError:F1} px，请补拍后重算"
            );
        }

        phaseSwS.Restart();
        using Mat map1x = new();
        using Mat map1y = new();
        using Mat map2x = new();
        using Mat map2y = new();

        Cv2.InitUndistortRectifyMap(
            mainCameraMatrix,
            mainDistCoeffs,
            r1,
            p1,
            imageSize,
            MatType.CV_32FC1,
            map1x,
            map1y
        );
        Cv2.InitUndistortRectifyMap(
            secondaryCameraMatrix,
            secondaryDistCoeffs,
            r2,
            p2,
            imageSize,
            MatType.CV_32FC1,
            map2x,
            map2y
        );
        _logger.LogInformation(
            "[双目标定] StereoRectify + InitUndistortRectifyMap 耗时 {Ms}ms",
            phaseSwS.ElapsedMilliseconds
        );

        phaseSwS.Restart();
        string map1XBlobKey = BuildStereoMapBlobKey(calibProjectId, "map1x");
        string map1YBlobKey = BuildStereoMapBlobKey(calibProjectId, "map1y");
        string map2XBlobKey = BuildStereoMapBlobKey(calibProjectId, "map2x");
        string map2YBlobKey = BuildStereoMapBlobKey(calibProjectId, "map2y");

        await _blobContainer.SaveAsync(map1XBlobKey, SerializeFloatMapToBinary(map1x), false);
        await _blobContainer.SaveAsync(map1YBlobKey, SerializeFloatMapToBinary(map1y), false);
        await _blobContainer.SaveAsync(map2XBlobKey, SerializeFloatMapToBinary(map2x), false);
        await _blobContainer.SaveAsync(map2YBlobKey, SerializeFloatMapToBinary(map2y), false);
        _logger.LogInformation(
            "[双目标定] 4张 map 序列化并写入 BLOB 耗时 {Ms}ms",
            phaseSwS.ElapsedMilliseconds
        );

        string rJson = SerializeMatToJson(r);
        string tJson = SerializeVecToJson(t);
        string lToRJson = SerializeTransformToJson(r, t);
        string rToLJson = SerializeInverseTransformToJson(r, t);
        string r1Json = SerializeMatToJson(r1);
        string r2Json = SerializeMatToJson(r2);
        string p1Json = SerializeMatToJson(p1);
        string p2Json = SerializeMatToJson(p2);

        IQueryable<CalibStereoResult> stereoQuery = await _stereoResultRepo.GetQueryableAsync();
        CalibStereoResult? stereoResult = await AsyncExecuter.FirstOrDefaultAsync(
            stereoQuery.Where(x => x.CalibProjectId == calibProjectId)
        );

        if (stereoResult == null)
        {
            stereoResult = new CalibStereoResult(
                GuidGenerator.Create(),
                calibProjectId,
                mainCameraId,
                secondaryCameraId,
                stereoError,
                rJson,
                tJson,
                lToRJson,
                rToLJson,
                r1Json,
                r2Json,
                p1Json,
                p2Json,
                imageSize.Width,
                imageSize.Height,
                map1XBlobKey,
                map1YBlobKey,
                map2XBlobKey,
                map2YBlobKey
            );
            await _stereoResultRepo.InsertAsync(stereoResult);
        }
        else
        {
            try
            {
                await _blobContainer.DeleteAsync(stereoResult.Map1XBlobKey);
                await _blobContainer.DeleteAsync(stereoResult.Map1YBlobKey);
                await _blobContainer.DeleteAsync(stereoResult.Map2XBlobKey);
                await _blobContainer.DeleteAsync(stereoResult.Map2YBlobKey);
            }
            catch
            {
                // 旧 map 可能不存在，忽略删除异常
            }

            stereoResult.SetResult(
                stereoError,
                rJson,
                tJson,
                lToRJson,
                rToLJson,
                r1Json,
                r2Json,
                p1Json,
                p2Json,
                imageSize.Width,
                imageSize.Height,
                map1XBlobKey,
                map1YBlobKey,
                map2XBlobKey,
                map2YBlobKey
            );
            await _stereoResultRepo.UpdateAsync(stereoResult);
        }

        _logger.LogInformation(
            "[双目标定] 全流程完成，总耗时 {TotalMs}ms，双目误差 {Error:F4} px",
            totalSwS.ElapsedMilliseconds,
            stereoError
        );

        return ToStereoResultDto(stereoResult);
    }

    /// <inheritdoc/>
    public async Task<CalibStereoStatusDto> GetStereoStatusAsync(Guid calibProjectId)
    {
        CalibProject project = await _projectRepo.GetAsync(calibProjectId);
        if (!project.MainCameraDeviceId.HasValue || !project.SecondaryCameraDeviceId.HasValue)
        {
            return new CalibStereoStatusDto();
        }

        Guid mainCameraId = project.MainCameraDeviceId.Value;
        Guid secondaryCameraId = project.SecondaryCameraDeviceId.Value;

        IQueryable<CalibPhotoRecord> query = await _photoRepo.GetQueryableAsync();
        List<CalibPhotoRecord> pairPhotos = await AsyncExecuter.ToListAsync(
            query.Where(x =>
                x.CalibProjectId == calibProjectId
                && x.PhotoType == CalibPhotoType.StereoExtrinsicPair
                && x.PairGroupId != null
            )
        );

        int pairTotal = pairPhotos
            .Where(x => x.CameraDeviceId == mainCameraId && x.StereoRole == StereoPhotoRole.Main)
            .Select(x => x.PairGroupId!.Value)
            .Distinct()
            .Count();

        int pairValid = pairPhotos
            .GroupBy(x => x.PairGroupId!.Value)
            .Count(group =>
                group.Any(x =>
                    x.CameraDeviceId == mainCameraId
                    && x.StereoRole == StereoPhotoRole.Main
                    && x.IsValid
                )
                && group.Any(x =>
                    x.CameraDeviceId == secondaryCameraId
                    && x.StereoRole == StereoPhotoRole.Secondary
                    && x.IsValid
                )
            );

        IQueryable<CalibStereoResult> stereoQuery = await _stereoResultRepo.GetQueryableAsync();
        CalibStereoResult? latestStereo = await AsyncExecuter.FirstOrDefaultAsync(
            stereoQuery.Where(x => x.CalibProjectId == calibProjectId)
        );

        return new CalibStereoStatusDto
        {
            PairTotal = pairTotal,
            PairValid = pairValid,
            LatestResult = latestStereo == null ? null : ToStereoResultDto(latestStereo),
        };
    }

    // ─── 私有辅助方法 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Step5 标定拍照专用帧抓取方法。
    /// 直接调用 ITucamCameraService，无需手动模式检查。
    /// 流程：读取并保存当前触发模式 → 切换到目标模式 →
    ///         StartCapture（获取单活锁）→ 必要时发软件触发 → GrabFrame → StopCapture（释放锁）→ 恢复触发模式。
    /// </summary>
    /// <param name="cameraDeviceId">相机设备 ID</param>
    /// <param name="targetTriggerMode">
    ///     0 = 自由运行；1 = 标准触发（硬件 IO）；2 = 软件触发
    /// </param>
    private async Task<byte[]> GrabCalibFrameRawAsync(Guid cameraDeviceId, int targetTriggerMode)
    {
        CameraDevice camera = await _cameraDeviceRepository.GetAsync(cameraDeviceId);
        int idx = camera.DeviceIndex;

        if (!_tucamService.IsCameraOpen(idx))
        {
            throw new UserFriendlyException(
                $"相机未打开（DeviceIndex={idx}），请先打开相机后再进行标定拍照"
            );
        }

        // 读取并保存原始触发模式（拍照完成后恢复）
        long originalTriggerMode = 0;
        try
        {
            originalTriggerMode = await _tucamService.GetGenICamIntAsync(idx, "TriggerMode");
        }
        catch
        { /* 读取失败按自由运行处理 */
        }

        // 切换触发模式（必须在 Cap_Start 之前执行）
        bool needRestore = originalTriggerMode != targetTriggerMode;
        if (needRestore)
        {
            await _tucamService.SetGenICamIntAsync(idx, "TriggerMode", targetTriggerMode);
            _logger.LogInformation(
                "标定拍照：相机 {Index} TriggerMode {Old} → {New}",
                idx,
                originalTriggerMode,
                targetTriggerMode
            );
        }

        try
        {
            // 单活锁保证：同一时刻仅一台相机处于 Cap_Start 活跃状态（USB 带宽限制）
            await _tucamService.StartCaptureAsync(idx);
            try
            {
                // 软件触发模式：发送 TriggerSoftwarePulse
                if (targetTriggerMode == 2)
                {
                    await _tucamService.DoSoftwareTriggerAsync(idx);
                }

                // 动态计算超时：曝光时间（微秒）× 2 + 1s，最小 8s；标准触发模式最小 15s
                int timeoutMs = targetTriggerMode == 1 ? 15000 : 8000;
                try
                {
                    long exposureUs = await _tucamService.GetGenICamIntAsync(idx, "ExposureTime");
                    timeoutMs = Math.Max((int)(exposureUs / 1000L) * 2 + 1000, timeoutMs);
                }
                catch
                { /* 读取失败使用默认超时 */
                }

                (byte[] jpegBytes, _) = await _tucamService.GrabFrameRawAsync(idx, timeoutMs);
                return jpegBytes;
            }
            finally
            {
                await _tucamService.StopCaptureAsync(idx);
            }
        }
        finally
        {
            // 恢复原始触发模式
            if (needRestore)
            {
                try
                {
                    await _tucamService.SetGenICamIntAsync(idx, "TriggerMode", originalTriggerMode);
                    _logger.LogInformation(
                        "标定拍照：相机 {Index} TriggerMode 已恢复为 {Original}",
                        idx,
                        originalTriggerMode
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "标定拍照：相机 {Index} 恢复触发模式失败", idx);
                }
            }
        }
    }

    /// <summary>从 data URI 提取 JPEG 二进制</summary>
    private static byte[] ExtractJpegBytes(string dataUri)
    {
        const string prefix = "data:image/jpeg;base64,";
        if (dataUri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return Convert.FromBase64String(dataUri[prefix.Length..]);
        }
        // 尝试直接 Base64
        return Convert.FromBase64String(dataUri);
    }

    /// <summary>构建标定照片的 BLOB Key</summary>
    private static string BuildBlobKey(Guid projectId, Guid cameraId, CalibPhotoType type)
    {
        string typeStr = type switch
        {
            CalibPhotoType.Intrinsic => "intrinsic",
            CalibPhotoType.Extrinsic => "extrinsic",
            CalibPhotoType.StereoExtrinsicPair => "stereo-pair",
            _ => "unknown",
        };
        string ts = DateTime.UtcNow.ToString("yyyyMMddHHmmss_fff");
        return $"{projectId}/{cameraId}/{typeStr}/{ts}.jpg";
    }

    /// <summary>OpenCV 棋盘格角点检测（使用亚像素精化）</summary>
    private (bool isValid, int cornerCount) DetectChessboardCorners(
        byte[] imageBytes,
        int cols,
        int rows
    )
    {
        try
        {
            Size patternSize = new(cols, rows);

            // 优先尝试 OpenCV 直接解码
            using Mat mat = Cv2.ImDecode(imageBytes, ImreadModes.Color);
            if (!mat.Empty())
            {
                // 先做一次降采样快速预检：若明确没有棋盘格特征，直接返回失败
                if (!HasChessboardFeatureQuickCheck(mat, patternSize))
                {
                    _logger.LogDebug(
                        "棋盘格快速预检未通过 (OpenCV解码路径): {Cols}x{Rows}，图像大小 {W}x{H}",
                        cols,
                        rows,
                        mat.Cols,
                        mat.Rows
                    );
                    return (false, 0);
                }

                _logger.LogDebug(
                    "棋盘格检测: OpenCV 解码成功 {W}x{H}，目标格式 {Cols}x{Rows}",
                    mat.Cols,
                    mat.Rows,
                    cols,
                    rows
                );
                Point2f[]? corners = FindCornersSubpix(mat, patternSize);
                if (corners != null)
                {
                    _logger.LogInformation(
                        "棋盘格检测成功 (OpenCV解码路径): 找到 {Count} 个角点",
                        corners.Length
                    );
                    return (true, corners.Length);
                }
                _logger.LogWarning(
                    "棋盘格检测失败 (OpenCV解码): {Cols}x{Rows} 角点未找到，图像大小 {W}x{H}",
                    cols,
                    rows,
                    mat.Cols,
                    mat.Rows
                );
                return (false, 0);
            }

            // OpenCV 解码失败（ARM64 JPEG 兼容问题）——改用 SkiaSharp 解码后转换为 BGR Mat
            _logger.LogDebug("棋盘格检测: OpenCV 解码失败，尝试 SkiaSharp fallback");
            using Mat skMat = SkiaBytesToBgrMat(imageBytes);
            if (skMat.Empty())
            {
                _logger.LogWarning("棋盘格检测: SkiaSharp 解码也失败，图像不可用");
                return (false, 0);
            }

            // 先做一次降采样快速预检：若明确没有棋盘格特征，直接返回失败
            if (!HasChessboardFeatureQuickCheck(skMat, patternSize))
            {
                _logger.LogDebug(
                    "棋盘格快速预检未通过 (SkiaSharp解码路径): {Cols}x{Rows}，图像大小 {W}x{H}",
                    cols,
                    rows,
                    skMat.Cols,
                    skMat.Rows
                );
                return (false, 0);
            }

            _logger.LogDebug(
                "棋盘格检测: SkiaSharp 解码成功 {W}x{H}，目标格式 {Cols}x{Rows}",
                skMat.Cols,
                skMat.Rows,
                cols,
                rows
            );
            Point2f[]? skCorners = FindCornersSubpix(skMat, patternSize);
            if (skCorners != null)
            {
                _logger.LogInformation(
                    "棋盘格检测成功 (SkiaSharp解码路径): 找到 {Count} 个角点",
                    skCorners.Length
                );
                return (true, skCorners.Length);
            }
            _logger.LogWarning(
                "棋盘格检测失败 (SkiaSharp解码): {Cols}x{Rows} 角点未找到，图像大小 {W}x{H}",
                cols,
                rows,
                skMat.Cols,
                skMat.Rows
            );
            return (false, 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "棋盘格角点检测异常 ({Cols}x{Rows})", cols, rows);
            return (false, 0);
        }
    }

    /// <summary>
    /// 降采样快速预检：用于快速过滤“完全没有棋盘格特征”的图像。
    /// 预检失败时可直接返回，不进入后续较重的多策略精检。
    /// </summary>
    private static bool HasChessboardFeatureQuickCheck(Mat mat, Size patternSize)
    {
        using Mat gray = new();
        if (mat.Channels() > 1)
        {
            Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY);
        }
        else
        {
            mat.CopyTo(gray);
        }

        // 将长边降到 640，降低预检开销
        const int maxSide = 640;
        using Mat preview = new();
        int longSide = Math.Max(gray.Cols, gray.Rows);
        if (longSide > maxSide)
        {
            double scale = maxSide / (double)longSide;
            int w = Math.Max(1, (int)Math.Round(gray.Cols * scale));
            int h = Math.Max(1, (int)Math.Round(gray.Rows * scale));
            Cv2.Resize(gray, preview, new Size(w, h), 0, 0, InterpolationFlags.Area);
        }
        else
        {
            gray.CopyTo(preview);
        }

        // FAST_CHECK: 仅做快速存在性判断，不做亚像素优化
        return Cv2.FindChessboardCorners(
            preview,
            patternSize,
            out _,
            ChessboardFlags.FastCheck
                | ChessboardFlags.AdaptiveThresh
                | ChessboardFlags.NormalizeImage
        );
    }

    /// <summary>
    /// 查找棋盘格角点并做亚像素精化。
    /// 按三个策略依次尝试，任意成功即返回；全部失败返回 null。
    /// 策略一：AdaptiveThresh + NormalizeImage（标准，最快）
    /// 策略二：加 FilterQuads（对复杂背景更鲁棒）
    /// 策略三：FindChessboardCornersSB（sub-pixel 搜索，对光照不均匀最鲁棒）
    /// </summary>
    private static Point2f[]? FindCornersSubpix(Mat mat, Size patternSize)
    {
        using Mat gray = new();
        if (mat.Channels() > 1)
        {
            Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY);
        }
        else
        {
            mat.CopyTo(gray);
        }

        // 策略一：标准 AdaptiveThresh + NormalizeImage
        if (
            Cv2.FindChessboardCorners(
                gray,
                patternSize,
                out Point2f[] corners1,
                ChessboardFlags.AdaptiveThresh | ChessboardFlags.NormalizeImage
            )
            && corners1.Length > 0
        )
        {
            Cv2.CornerSubPix(
                gray,
                corners1,
                new Size(11, 11),
                new Size(-1, -1),
                new TermCriteria(CriteriaTypes.Eps | CriteriaTypes.MaxIter, 30, 0.001)
            );
            return corners1;
        }

        // 策略二：增加 FilterQuads，对复杂背景有改善
        if (
            Cv2.FindChessboardCorners(
                gray,
                patternSize,
                out Point2f[] corners2,
                ChessboardFlags.AdaptiveThresh
                    | ChessboardFlags.NormalizeImage
                    | ChessboardFlags.FilterQuads
            )
            && corners2.Length > 0
        )
        {
            Cv2.CornerSubPix(
                gray,
                corners2,
                new Size(11, 11),
                new Size(-1, -1),
                new TermCriteria(CriteriaTypes.Eps | CriteriaTypes.MaxIter, 30, 0.001)
            );
            return corners2;
        }

        // 策略三：FindChessboardCornersSB，对光照不均匀最鲁棒（已内含亚像素精化）
        if (
            Cv2.FindChessboardCornersSB(gray, patternSize, out Point2f[] corners3)
            && corners3.Length > 0
        )
        {
            return corners3;
        }

        return null;
    }

    /// <summary>
    /// 在灰度图上查找棋盘格角点（含亚像素精化），专为计算流程的高分辨率加速设计。
    /// 优先在 50% 缩放图上全策略检测（含 FindChessboardCornersSB），速度约为全分辨率 4 倍；
    /// 找到后将坐标 ×2 还原至原分辨率并做亚像素精化（精度等价于全分辨率直接检测）；
    /// 半分辨率全部失败时退回全分辨率策略 1+2（不再跑 SB，避免 RK3588 上 10~18 秒的超时）。
    /// </summary>
    private static Point2f[]? FindCornersSubpixGray(Mat grayFull, Size patternSize)
    {
        ChessboardFlags flags1 = ChessboardFlags.AdaptiveThresh | ChessboardFlags.NormalizeImage;
        ChessboardFlags flags2 = flags1 | ChessboardFlags.FilterQuads;
        TermCriteria subPixCrit = new(CriteriaTypes.Eps | CriteriaTypes.MaxIter, 30, 0.001);

        // ── 步骤一：50% 缩放图检测（速度约 4× 快）───────────────────────────────
        int hw = Math.Max(1, grayFull.Cols / 2);
        int hh = Math.Max(1, grayFull.Rows / 2);
        using Mat grayHalf = new();
        Cv2.Resize(grayFull, grayHalf, new Size(hw, hh), 0, 0, InterpolationFlags.Area);

        Point2f[]? halfFound = null;
        if (
            Cv2.FindChessboardCorners(grayHalf, patternSize, out Point2f[] c1h, flags1)
            && c1h.Length > 0
        )
            halfFound = c1h;
        else if (
            Cv2.FindChessboardCorners(grayHalf, patternSize, out Point2f[] c2h, flags2)
            && c2h.Length > 0
        )
            halfFound = c2h;
        else if (
            Cv2.FindChessboardCornersSB(grayHalf, patternSize, out Point2f[] c3h)
            && c3h.Length > 0
        )
            halfFound = c3h;

        if (halfFound != null)
        {
            // 将半分辨率坐标还原至全分辨率，再做亚像素精化
            // 缩放误差 ≤ ±1px，CornerSubPix 窗口 11×11（半径 5.5px）可完全覆盖
            for (int i = 0; i < halfFound.Length; i++)
                halfFound[i] = new Point2f(halfFound[i].X * 2f, halfFound[i].Y * 2f);
            Cv2.CornerSubPix(grayFull, halfFound, new Size(11, 11), new Size(-1, -1), subPixCrit);
            return halfFound;
        }

        // ── 步骤二：全分辨率兜底（策略 1+2，不含 SB，避免 RK3588 超时）──────────
        if (
            Cv2.FindChessboardCorners(grayFull, patternSize, out Point2f[] cf1, flags1)
            && cf1.Length > 0
        )
        {
            Cv2.CornerSubPix(grayFull, cf1, new Size(11, 11), new Size(-1, -1), subPixCrit);
            return cf1;
        }
        if (
            Cv2.FindChessboardCorners(grayFull, patternSize, out Point2f[] cf2, flags2)
            && cf2.Length > 0
        )
        {
            Cv2.CornerSubPix(grayFull, cf2, new Size(11, 11), new Size(-1, -1), subPixCrit);
            return cf2;
        }

        return null;
    }

    /// <summary>生成缩略图 Base64（宽 400px，保持比例）——优先 SkiaSharp，避免 OpenCV ARM64 JPEG 兼容问题</summary>
    private static string? GenerateThumbnailBase64(byte[] imageBytes)
    {
        try
        {
            // 使用 SkiaSharp 解码（与 Tucam JPEG 编码器一致，就算是硬件 JPEG 也能解码）
            using SKBitmap? bitmap = SKBitmap.Decode(imageBytes);
            if (bitmap == null || bitmap.IsNull)
                return null;

            int targetWidth = 400;
            int targetHeight = Math.Max(
                1,
                (int)(bitmap.Height * (targetWidth / (double)bitmap.Width))
            );

            SKImageInfo info = new(
                targetWidth,
                targetHeight,
                SKColorType.Bgra8888,
                SKAlphaType.Opaque
            );
            using SKBitmap resized = new(info);
            bitmap.ScalePixels(resized, new SKSamplingOptions(SKFilterMode.Linear));

            using SKImage skImg = SKImage.FromBitmap(resized);
            using SKData? encoded = skImg.Encode(SKEncodedImageFormat.Jpeg, 70);
            if (encoded == null)
                return null;

            return $"data:image/jpeg;base64,{Convert.ToBase64String(encoded.ToArray())}";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>构建世界坐标系中的棋盘格角点（Z=0 平面）</summary>
    private static Point3f[] BuildWorldCorners(int cols, int rows, float squareSizeMm)
    {
        Point3f[] points = new Point3f[cols * rows];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                points[r * cols + c] = new Point3f(c * squareSizeMm, r * squareSizeMm, 0f);
            }
        }
        return points;
    }

    /// <summary>从多张外参照片中找重投影误差最小的一张</summary>
    private async Task<CalibPhotoRecord?> FindBestExtrinsicAsync(
        List<CalibPhotoRecord> photos,
        Mat cameraMatrix,
        Mat distCoeffs,
        Size patternSize,
        Point3f[] worldCorners
    )
    {
        CalibPhotoRecord? best = null;
        double bestError = double.MaxValue;

        foreach (CalibPhotoRecord photo in photos)
        {
            try
            {
                byte[] bytes = await _blobContainer.GetAllBytesAsync(photo.BlobKey);

                // 灰度加载 + 半分辨率加速角点检测
                using Mat mat = LoadGrayMat(bytes);
                if (mat.Empty())
                    continue;

                Point2f[]? corners = FindCornersSubpixGray(mat, patternSize);
                if (corners == null)
                    continue;

                using Mat rvec = new();
                using Mat tvec = new();
                // SolvePnP 需要 InputArray
                Cv2.SolvePnP(
                    InputArray.Create(worldCorners),
                    InputArray.Create(corners),
                    cameraMatrix,
                    distCoeffs,
                    rvec,
                    tvec
                );

                // ProjectPoints 第6参数为 OutputArray（不能用 out 关键字）
                using Mat projectedMat = new();
                Cv2.ProjectPoints(
                    InputArray.Create(worldCorners),
                    rvec,
                    tvec,
                    cameraMatrix,
                    distCoeffs,
                    projectedMat
                );
                projectedMat.GetArray(out Point2f[] projected);

                double err = 0;
                for (int i = 0; i < corners.Length; i++)
                {
                    double dx = corners[i].X - projected[i].X;
                    double dy = corners[i].Y - projected[i].Y;
                    err += Math.Sqrt(dx * dx + dy * dy);
                }
                err /= corners.Length;

                if (err < bestError)
                {
                    bestError = err;
                    best = photo;
                }
            }
            catch
            {
                // 忽略单张失败
            }
        }

        return best;
    }

    /// <summary>OpenCV 不能解码时用 SkiaSharp 解码并转换为 BGR Mat</summary>
    private static Mat SkiaBytesToBgrMat(byte[] imageBytes)
    {
        try
        {
            using SKBitmap? skBitmap = SKBitmap.Decode(imageBytes);
            if (skBitmap == null || skBitmap.IsNull)
                return new Mat();

            // 确保 Bgra8888 格式（内存布局：[B,G,R,A] 每像素 4 字节）
            using SKBitmap bgra =
                skBitmap.ColorType == SKColorType.Bgra8888
                    ? skBitmap.Copy()
                    : skBitmap.Copy(SKColorType.Bgra8888);

            int width = bgra.Width;
            int height = bgra.Height;
            byte[] bgraBytes = bgra.Bytes;

            // BGRA → BGR：去掉 Alpha 通道
            byte[] bgrBytes = new byte[width * height * 3];
            for (int i = 0, src = 0; src < bgraBytes.Length; i += 3, src += 4)
            {
                bgrBytes[i + 0] = bgraBytes[src + 0]; // B
                bgrBytes[i + 1] = bgraBytes[src + 1]; // G
                bgrBytes[i + 2] = bgraBytes[src + 2]; // R
            }

            Mat mat = new Mat(height, width, MatType.CV_8UC3);
            Marshal.Copy(bgrBytes, 0, mat.Data, bgrBytes.Length);
            return mat;
        }
        catch
        {
            return new Mat();
        }
    }

    /// <summary>
    /// 加载图像为 BGR Mat：优先 OpenCV，失败则用 SkiaSharp（ARM64 硬件 JPEG 兼容性保障）
    /// </summary>
    private static Mat LoadBgrMat(byte[] imageBytes)
    {
        using Mat mat = Cv2.ImDecode(imageBytes, ImreadModes.Color);
        if (!mat.Empty())
        {
            // 返回副本避免 using 释放
            return mat.Clone();
        }
        return SkiaBytesToBgrMat(imageBytes);
    }

    /// <summary>
    /// 以灰度模式加载图像，比 BGR 更快，可直接送入角点检测，避免后续 BGR→Gray 转换开销。
    /// ARM64 JPEG 解码失败时自动用 SkiaSharp 兜底。
    /// </summary>
    private static Mat LoadGrayMat(byte[] imageBytes)
    {
        using Mat mat = Cv2.ImDecode(imageBytes, ImreadModes.Grayscale);
        if (!mat.Empty())
            return mat.Clone();

        // SkiaSharp fallback：转为 Gray8 单通道
        try
        {
            using SKBitmap? skBitmap = SKBitmap.Decode(imageBytes);
            if (skBitmap == null || skBitmap.IsNull)
                return new Mat();

            using SKBitmap gray8 = skBitmap.Copy(SKColorType.Gray8); // 1 byte/pixel
            byte[] grayBytes = gray8.Bytes;
            Mat grayMat = new(gray8.Height, gray8.Width, MatType.CV_8UC1);
            Marshal.Copy(grayBytes, 0, grayMat.Data, grayBytes.Length);
            return grayMat;
        }
        catch
        {
            return new Mat();
        }
    }

    private static string SerializeMatToJson(Mat mat)
    {
        int rows = mat.Rows;
        int cols = mat.Cols;
        double[][] arr = new double[rows][];
        for (int r = 0; r < rows; r++)
        {
            arr[r] = new double[cols];
            for (int c = 0; c < cols; c++)
            {
                arr[r][c] = mat.At<double>(r, c);
            }
        }
        return JsonSerializer.Serialize(arr);
    }

    /// <summary>将 OpenCV 向量 Mat (N×1 或 1×N) 序列化为 JSON double[]</summary>
    private static string SerializeVecToJson(Mat mat)
    {
        int total = (int)mat.Total();
        double[] arr = new double[total];
        for (int i = 0; i < total; i++)
        {
            // 支持列向量 (N,1) 或行向量 (1,N)
            arr[i] = mat.Rows > 1 ? mat.At<double>(i, 0) : mat.At<double>(0, i);
        }
        return JsonSerializer.Serialize(arr);
    }

    /// <summary>将 JSON double[][] 反序列化为 CV_64F 矩阵</summary>
    private static Mat DeserializeMatrix(string json)
    {
        double[][]? arr = JsonSerializer.Deserialize<double[][]>(json);
        if (arr == null || arr.Length == 0 || arr[0].Length == 0)
        {
            throw new UserFriendlyException("标定矩阵数据格式无效");
        }

        int rows = arr.Length;
        int cols = arr[0].Length;
        Mat mat = new(rows, cols, MatType.CV_64FC1);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                mat.Set(r, c, arr[r][c]);
            }
        }
        return mat;
    }

    /// <summary>将 JSON double[] 反序列化为 CV_64F 列向量</summary>
    private static Mat DeserializeVector(string json)
    {
        double[]? arr = JsonSerializer.Deserialize<double[]>(json);
        if (arr == null || arr.Length == 0)
        {
            throw new UserFriendlyException("标定向量数据格式无效");
        }

        Mat mat = new(arr.Length, 1, MatType.CV_64FC1);
        for (int i = 0; i < arr.Length; i++)
        {
            mat.Set(i, 0, arr[i]);
        }
        return mat;
    }

    /// <summary>将 R、t 序列化为左到右 4x4 齐次变换矩阵 JSON</summary>
    private static string SerializeTransformToJson(Mat rotation, Mat translation)
    {
        double[][] transform =
        [
            [
                rotation.At<double>(0, 0),
                rotation.At<double>(0, 1),
                rotation.At<double>(0, 2),
                translation.At<double>(0, 0),
            ],
            [
                rotation.At<double>(1, 0),
                rotation.At<double>(1, 1),
                rotation.At<double>(1, 2),
                translation.At<double>(1, 0),
            ],
            [
                rotation.At<double>(2, 0),
                rotation.At<double>(2, 1),
                rotation.At<double>(2, 2),
                translation.At<double>(2, 0),
            ],
            [0.0, 0.0, 0.0, 1.0],
        ];
        return JsonSerializer.Serialize(transform);
    }

    /// <summary>将 R、t 序列化为右到左 4x4 齐次变换矩阵 JSON</summary>
    private static string SerializeInverseTransformToJson(Mat rotation, Mat translation)
    {
        using Mat rT = rotation.T();
        using Mat tNeg = new();
        using Mat zero = Mat.Zeros(rT.Rows, translation.Cols, MatType.CV_64FC1);
        Cv2.Gemm(rT, translation, -1.0, zero, 0.0, tNeg);

        double[][] transform =
        [
            [rT.At<double>(0, 0), rT.At<double>(0, 1), rT.At<double>(0, 2), tNeg.At<double>(0, 0)],
            [rT.At<double>(1, 0), rT.At<double>(1, 1), rT.At<double>(1, 2), tNeg.At<double>(1, 0)],
            [rT.At<double>(2, 0), rT.At<double>(2, 1), rT.At<double>(2, 2), tNeg.At<double>(2, 0)],
            [0.0, 0.0, 0.0, 1.0],
        ];
        return JsonSerializer.Serialize(transform);
    }

    /// <summary>将 CV_32FC1 map 序列化为二进制（头: rows, cols；体: float32 连续数据）</summary>
    private static byte[] SerializeFloatMapToBinary(Mat map)
    {
        if (map.Type() != MatType.CV_32FC1)
        {
            throw new UserFriendlyException("立体校正 map 数据类型无效");
        }

        int rows = map.Rows;
        int cols = map.Cols;
        using MemoryStream ms = new();
        using BinaryWriter bw = new(ms);

        bw.Write(rows);
        bw.Write(cols);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                bw.Write(map.At<float>(r, c));
            }
        }

        return ms.ToArray();
    }

    /// <summary>构建双目立体校正 map 的 BLOB Key</summary>
    private static string BuildStereoMapBlobKey(Guid projectId, string mapName)
    {
        string ts = DateTime.UtcNow.ToString("yyyyMMddHHmmss_fff");
        return $"{projectId}/stereo-maps/{mapName}_{ts}.bin";
    }

    // ─── DTO 转换 ─────────────────────────────────────────────────────────────

    private static CalibBoardConfigDto ToBoardConfigDto(CalibProject project) =>
        new()
        {
            PhysicalCornerRows = project.PhysicalCornerRows,
            PhysicalCornerCols = project.PhysicalCornerCols,
            PhysicalSquareSizeMm = project.PhysicalSquareSizeMm,
            ProjectedCornerRows = project.ProjectedCornerRows,
            ProjectedCornerCols = project.ProjectedCornerCols,
            ProjectedPixelSize = project.ProjectedPixelSize,
        };

    private static CalibPhotoDto ToPhotoDto(CalibPhotoRecord record) =>
        new()
        {
            Id = record.Id,
            PhotoType = record.PhotoType,
            IsValid = record.IsValid,
            CornerCountDetected = record.CornerCountDetected,
            CapturedAt = record.CapturedAt,
            ThumbnailBase64 = record.ThumbnailBase64,
            PairGroupId = record.PairGroupId,
            StereoRole = record.StereoRole,
        };

    private static CalibStereoComputeResultDto ToStereoResultDto(CalibStereoResult result) =>
        new()
        {
            MainCameraDeviceId = result.MainCameraDeviceId,
            SecondaryCameraDeviceId = result.SecondaryCameraDeviceId,
            StereoReprojectionError = result.StereoReprojectionError,
            RotationMatrixJson = result.RotationMatrixJson,
            TranslationVectorJson = result.TranslationVectorJson,
            TransformLtoRJson = result.TransformLtoRJson,
            TransformRtoLJson = result.TransformRtoLJson,
            RectificationR1Json = result.RectificationR1Json,
            RectificationR2Json = result.RectificationR2Json,
            ProjectionP1Json = result.ProjectionP1Json,
            ProjectionP2Json = result.ProjectionP2Json,
            RectifyMapWidth = result.RectifyMapWidth,
            RectifyMapHeight = result.RectifyMapHeight,
            Map1XBlobKey = result.Map1XBlobKey,
            Map1YBlobKey = result.Map1YBlobKey,
            Map2XBlobKey = result.Map2XBlobKey,
            Map2YBlobKey = result.Map2YBlobKey,
        };
}
