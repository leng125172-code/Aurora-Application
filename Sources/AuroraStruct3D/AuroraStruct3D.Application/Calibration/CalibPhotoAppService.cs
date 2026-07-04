using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AuroraStruct3D.Calibration.Dtos;
using AuroraStruct3D.Cameras;
using AuroraStruct3D.Cameras.Dtos;
using AuroraStruct3D.Motors;
using AuroraStruct3D.Projectors;
using AuroraStruct3D.Projectors.Dtos;
using AuroraStruct3D.RS485;
using AuroraStruct3D.RS485.Ktech;
using AuroraStruct3D.Tucam;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using SkiaSharp;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// 标定照片管理应用服务实现（Step 5）。
/// 负责：棋盘格参数配置、内参/外参拍照（含 OpenCV 角点检测）、照片查询/删除、内外参计算。
/// </summary>
[Authorize]
public class CalibPhotoAppService : AuroraStruct3DAppService, ICalibPhotoAppService
{
    private sealed class ProjectorExtrinsicSampleGroup
    {
        public required Guid PairGroupId { get; init; }
        public required CalibPhotoRecord ProjectorOffPhoto { get; init; }
        public required CalibPhotoRecord ProjectorOnPhoto { get; init; }

        public bool IsValid => ProjectorOffPhoto.IsValid && ProjectorOnPhoto.IsValid;
    }

    private readonly IRepository<CalibProject, Guid> _projectRepo;
    private readonly IRepository<CalibCameraParam, Guid> _cameraParamRepo;
    private readonly IRepository<CalibPhotoRecord, Guid> _photoRepo;
    private readonly IRepository<CalibStereoResult, Guid> _stereoResultRepo;
    private readonly IBlobContainer<CalibPhotoBlobContainer> _blobContainer;
    private readonly IRepository<CameraDevice, Guid> _cameraDeviceRepository;
    private readonly ITucamCameraService _tucamService;
    private readonly IProjectorDeviceAppService _projectorService;
    private readonly IMotorAxisRepository _motorAxisRepository;
    private readonly IMotorControlService _motorControlService;
    private readonly IRepository<CalibMotorParam, Guid> _motorParamRepo;
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
        IMotorAxisRepository motorAxisRepository,
        IMotorControlService motorControlService,
        IRepository<CalibMotorParam, Guid> motorParamRepo,
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
        _motorAxisRepository = motorAxisRepository;
        _motorControlService = motorControlService;
        _motorParamRepo = motorParamRepo;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<CalibBoardConfigDto> UpdateBoardConfigAsync(UpdateBoardConfigInput input)
    {
        CalibProject project = await _projectRepo.GetAsync(input.CalibProjectId);

        CircleBoardConfigDto? circle = input.CircleBoardConfig;
        if (input.BoardType != CalibrationBoardType.Chessboard && circle == null)
        {
            circle = BuildDefaultCircleBoardConfig(input.BoardType);
        }
        CirclePatternSizeDto? circlePattern = circle?.PatternSize;
        CircleMarkerPositionDto? marker = circle?.MarkerPosition;

        project.SetBoardConfig(
            input.BoardType,
            input.PhysicalCornerRows,
            input.PhysicalCornerCols,
            input.PhysicalSquareSizeMm,
            input.ProjectedCornerRows,
            input.ProjectedCornerCols,
            input.ProjectedPixelSize,
            circlePattern?.Width,
            circlePattern?.Height,
            circle?.CircleSpacing,
            circle?.CircleDiameter,
            circle?.HasCenterMarker,
            circle?.HasCornerLocators,
            marker?.Row,
            marker?.Col,
            circle?.Detector is null ? null : JsonSerializer.Serialize(circle.Detector)
        );

        await _projectRepo.UpdateAsync(project);
        return ToBoardConfigDto(project);
    }

    private static CircleBoardConfigDto BuildDefaultCircleBoardConfig(
        CalibrationBoardType boardType
    )
    {
        bool marked = boardType == CalibrationBoardType.MarkedSymmetricCircleGrid;
        return new CircleBoardConfigDto
        {
            PatternSize = new CirclePatternSizeDto { Width = 27, Height = 27 },
            CircleSpacing = 10m,
            CircleDiameter = 3m,
            HasCenterMarker = marked,
            HasCornerLocators = false,
            MarkerPosition = new CircleMarkerPositionDto { Row = 13, Col = 13 },
            Detector = CircleBlobDetectorConfigDto.CreateDefault(),
        };
    }

    /// <inheritdoc/>
    public async Task<CalibBoardConfigDto> GetBoardConfigAsync(Guid calibProjectId)
    {
        CalibProject project = await _projectRepo.GetAsync(calibProjectId);
        return ToBoardConfigDto(project);
    }

    /// <inheritdoc/>
    public async Task<IRemoteStreamContent> ExportBoardConfigAsync(Guid calibProjectId)
    {
        CalibProject project = await _projectRepo.GetAsync(calibProjectId);
        CalibBoardConfigDto dto = ToBoardConfigDto(project);
        string json = JsonSerializer.Serialize(
            dto,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            }
        );

        byte[] bytes = Encoding.UTF8.GetBytes(json);
        MemoryStream stream = new(bytes);
        string fileName = $"calib-board-config-{calibProjectId:N}.json";
        return new RemoteStreamContent(stream, fileName, "application/json");
    }

    /// <inheritdoc/>
    public async Task<CalibBoardConfigDto> ImportBoardConfigAsync(
        Guid calibProjectId,
        IRemoteStreamContent file
    )
    {
        Check.NotNull(file, nameof(file));

        await using Stream stream = file.GetStream();
        using StreamReader reader = new(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true
        );
        string json = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(json))
            throw new UserFriendlyException("导入失败：配置文件为空");

        CalibBoardConfigDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<CalibBoardConfigDto>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    NumberHandling = JsonNumberHandling.AllowReadingFromString,
                }
            );
        }
        catch (JsonException ex)
        {
            throw new UserFriendlyException(
                "导入失败：配置文件不是有效的 JSON",
                innerException: ex
            );
        }

        if (dto is null)
            throw new UserFriendlyException("导入失败：无法解析标定板配置");

        UpdateBoardConfigInput input = new()
        {
            CalibProjectId = calibProjectId,
            BoardType = dto.BoardType,
            PhysicalCornerRows = dto.PhysicalCornerRows,
            PhysicalCornerCols = dto.PhysicalCornerCols,
            PhysicalSquareSizeMm = dto.PhysicalSquareSizeMm,
            ProjectedCornerRows = dto.ProjectedCornerRows,
            ProjectedCornerCols = dto.ProjectedCornerCols,
            ProjectedPixelSize = dto.ProjectedPixelSize,
            CircleBoardConfig = dto.CircleBoardConfig,
        };

        return await UpdateBoardConfigAsync(input);
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

        // 有结构光：开灯并切换到白屏，为内参拍照提供均匀背景光
        if (input.ProjectorDeviceId.HasValue && project.DeviceSeries == DeviceSeries.SingleLight)
        {
            Guid projId = input.ProjectorDeviceId.Value;
            await _projectorService.LedOnAsync(projId);
            await _projectorService.SetDisplayModeAsync(
                new SetProjectorDisplayModeDto
                {
                    ProjectorDeviceId = projId,
                    Mode = ProjectorDisplayMode.White,
                }
            );
            await Task.Delay(200);
        }

        // 内参拍照：切换到软件触发模式，避免自由运行模式下相机持续输出干扰拍照
        byte[] jpegBytes;
        try
        {
            jpegBytes = await GrabCalibFrameRawAsync(input.CameraDeviceId, targetTriggerMode: 2);
        }
        finally
        {
            // 拍完立即关灯（无论拍照是否成功）
            if (
                input.ProjectorDeviceId.HasValue
                && project.DeviceSeries == DeviceSeries.SingleLight
            )
            {
                await _projectorService.LedOffAsync(input.ProjectorDeviceId.Value);
            }
        }

        // OpenCV 棋盘格角点检测
        (bool isValid, int cornerCount) = DetectBoardFeaturePoints(
            jpegBytes,
            project,
            isProjectedBoard: false
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
    public async Task<CalibExtrinsicSampleDto> TakeExtrinsicPhotoAsync(
        TakeExtrinsicPhotoInput input
    )
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
        Guid pairGroupId = GuidGenerator.Create();

        byte[] projectorOffBytes;
        byte[] projectorOnBytes;
        try
        {
            await _projectorService.LedOnAsync(projectorDeviceId);
            await _projectorService.SetDisplayModeAsync(
                new SetProjectorDisplayModeDto
                {
                    ProjectorDeviceId = projectorDeviceId,
                    Mode = ProjectorDisplayMode.White,
                }
            );
            await Task.Delay(200);

            projectorOffBytes = await GrabCalibFrameRawAsync(
                input.CameraDeviceId,
                targetTriggerMode: 2
            );

            await _projectorService.SetDisplayModeAsync(
                new SetProjectorDisplayModeDto
                {
                    ProjectorDeviceId = projectorDeviceId,
                    Mode = ProjectorDisplayMode.Checkerboard,
                }
            );
            await _projectorService.SetCheckerboardPixelSizeAsync(
                new SetProjectorCheckerboardDto
                {
                    ProjectorDeviceId = projectorDeviceId,
                    PixelSize = project.ProjectedPixelSize > 0 ? project.ProjectedPixelSize : 30,
                }
            );
            await Task.Delay(200);

            projectorOnBytes = await GrabCalibFrameRawAsync(
                input.CameraDeviceId,
                targetTriggerMode: 2
            );
        }
        finally
        {
            await _projectorService.LedOffAsync(projectorDeviceId);
        }

        (bool projectorOffValid, int projectorOffCornerCount) = DetectBoardFeaturePoints(
            projectorOffBytes,
            project,
            isProjectedBoard: false
        );

        (bool projectorOnValid, int projectorOnCornerCount) = DetectBoardFeaturePoints(
            projectorOnBytes,
            project,
            isProjectedBoard: true
        );

        string projectorOffBlobKey = BuildBlobKey(
            input.CalibProjectId,
            input.CameraDeviceId,
            CalibPhotoType.Extrinsic,
            ExtrinsicPhotoPhase.ProjectorOff
        );
        string projectorOnBlobKey = BuildBlobKey(
            input.CalibProjectId,
            input.CameraDeviceId,
            CalibPhotoType.Extrinsic,
            ExtrinsicPhotoPhase.ProjectorOn
        );

        await _blobContainer.SaveAsync(
            projectorOffBlobKey,
            projectorOffBytes,
            overrideExisting: false
        );
        await _blobContainer.SaveAsync(
            projectorOnBlobKey,
            projectorOnBytes,
            overrideExisting: false
        );

        CalibPhotoRecord projectorOffRecord = new(
            GuidGenerator.Create(),
            input.CalibProjectId,
            input.CameraDeviceId,
            CalibPhotoType.Extrinsic,
            projectorOffBlobKey,
            projectorOffValid,
            projectorOffCornerCount,
            GenerateThumbnailBase64(projectorOffBytes),
            pairGroupId,
            stereoRole: null,
            extrinsicPhase: ExtrinsicPhotoPhase.ProjectorOff
        );
        CalibPhotoRecord projectorOnRecord = new(
            GuidGenerator.Create(),
            input.CalibProjectId,
            input.CameraDeviceId,
            CalibPhotoType.Extrinsic,
            projectorOnBlobKey,
            projectorOnValid,
            projectorOnCornerCount,
            GenerateThumbnailBase64(projectorOnBytes),
            pairGroupId,
            stereoRole: null,
            extrinsicPhase: ExtrinsicPhotoPhase.ProjectorOn
        );

        await _photoRepo.InsertAsync(projectorOffRecord);
        await _photoRepo.InsertAsync(projectorOnRecord);

        return ToExtrinsicSampleDto(pairGroupId, projectorOffRecord, projectorOnRecord);
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

        // 若项目绑定了投影仪，开灯并切换到白屏，为双目成对拍照提供均匀背景光
        Guid? projectorId = project.BoundProjectorDeviceId;
        if (projectorId.HasValue)
        {
            await _projectorService.LedOnAsync(projectorId.Value);
            await _projectorService.SetDisplayModeAsync(
                new SetProjectorDisplayModeDto
                {
                    ProjectorDeviceId = projectorId.Value,
                    Mode = ProjectorDisplayMode.White,
                }
            );
            await Task.Delay(200);
        }

        // 主相机先拍，从相机再拍（_capStartActiveLock 保证顺序执行）
        // 两台均切换到软件触发模式进行标定拍照
        byte[] mainBytes;
        byte[] secondaryBytes;
        try
        {
            mainBytes = await GrabCalibFrameRawAsync(mainCameraId, targetTriggerMode: 2);
            secondaryBytes = await GrabCalibFrameRawAsync(secondaryCameraId, targetTriggerMode: 2);
        }
        finally
        {
            // 拍完立即关灯（无论拍照是否成功）
            if (projectorId.HasValue)
            {
                await _projectorService.LedOffAsync(projectorId.Value);
            }
        }

        (bool mainValid, int mainCornerCount) = DetectBoardFeaturePoints(
            mainBytes,
            project,
            isProjectedBoard: false
        );
        (bool secondaryValid, int secondaryCornerCount) = DetectBoardFeaturePoints(
            secondaryBytes,
            project,
            isProjectedBoard: false
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
        if (record.PhotoType == CalibPhotoType.Extrinsic && record.PairGroupId.HasValue)
        {
            IQueryable<CalibPhotoRecord> query = await _photoRepo.GetQueryableAsync();
            List<CalibPhotoRecord> groupedPhotos = await AsyncExecuter.ToListAsync(
                query.Where(x =>
                    x.CalibProjectId == record.CalibProjectId
                    && x.CameraDeviceId == record.CameraDeviceId
                    && x.PhotoType == CalibPhotoType.Extrinsic
                    && x.PairGroupId == record.PairGroupId
                )
            );

            await DeletePhotoRecordsAsync(groupedPhotos);
            return;
        }

        await DeletePhotoRecordsAsync(new[] { record });
    }

    /// <inheritdoc/>
    public async Task<int> DeleteInvalidPhotosAsync(Guid calibProjectId, Guid cameraDeviceId)
    {
        IQueryable<CalibPhotoRecord> query = await _photoRepo.GetQueryableAsync();
        List<CalibPhotoRecord> allPhotos = await AsyncExecuter.ToListAsync(
            query.Where(x =>
                x.CalibProjectId == calibProjectId && x.CameraDeviceId == cameraDeviceId
            )
        );

        List<CalibPhotoRecord> invalidPhotos = allPhotos
            .Where(x => x.PhotoType != CalibPhotoType.Extrinsic && !x.IsValid)
            .ToList();

        List<CalibPhotoRecord> extrinsicPhotos = allPhotos
            .Where(x => x.PhotoType == CalibPhotoType.Extrinsic)
            .ToList();
        HashSet<Guid> invalidExtrinsicGroupIds = BuildExtrinsicSampleGroups(extrinsicPhotos)
            .Where(x => !x.IsValid)
            .Select(x => x.PairGroupId)
            .ToHashSet();

        invalidPhotos.AddRange(
            extrinsicPhotos.Where(x =>
                (x.PairGroupId.HasValue && invalidExtrinsicGroupIds.Contains(x.PairGroupId.Value))
                || (!x.PairGroupId.HasValue && !x.IsValid)
            )
        );

        if (invalidPhotos.Count == 0)
            return 0;

        await DeletePhotoRecordsAsync(invalidPhotos.DistinctBy(x => x.Id).ToList());

        _logger.LogInformation(
            "删除无效照片: 项目 {ProjectId} 相机 {CameraId} 共删除 {Count} 张",
            calibProjectId,
            cameraDeviceId,
            invalidPhotos.Count
        );

        return invalidPhotos.Count;
    }

    /// <inheritdoc/>
    public async Task<CalibComputeResultDto> ComputeIntrinsicAsync(
        Guid calibProjectId,
        Guid cameraDeviceId
    )
    {
        _logger.LogInformation(
            "[内参标定] 开始计算 — 项目 {ProjectId} 相机 {CameraId}",
            calibProjectId,
            cameraDeviceId
        );

        CalibProject project = await _projectRepo.GetAsync(calibProjectId);

        EnsureBoardConfigValid(project, isProjectedBoard: false);

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

        if (intrinsicPhotos.Count < CalibConsts.MinValidPhotoCount)
        {
            throw new UserFriendlyException(
                $"内参有效照片不足 {CalibConsts.MinValidPhotoCount} 张（当前 {intrinsicPhotos.Count} 张）"
            );
        }

        Size patternSize = GetBoardPatternSize(project, isProjectedBoard: false);
        float spacingMm = GetBoardSpacingMm(project);
        Point3f[] worldCorners = BuildBoardWorldPoints(project, patternSize, spacingMm);

        List<Mat> objectMats = [];
        List<Mat> imageMats = [];
        Size imageSize = default;

        for (int idx = 0; idx < intrinsicPhotos.Count; idx++)
        {
            CalibPhotoRecord photo = intrinsicPhotos[idx];
            byte[] bytes = await _blobContainer.GetAllBytesAsync(photo.BlobKey);
            using Mat mat = LoadGrayMat(bytes);
            if (mat.Empty())
                continue;
            if (imageSize == default)
                imageSize = new Size(mat.Cols, mat.Rows);
            Point2f[]? corners = FindBoardPointsSubpixGray(
                mat,
                patternSize,
                project.BoardType,
                project
            );
            if (corners == null)
                continue;
            objectMats.Add(Mat.FromArray(worldCorners));
            imageMats.Add(Mat.FromArray(corners));
        }

        if (objectMats.Count < CalibConsts.MinValidPhotoCount)
        {
            foreach (Mat m in objectMats)
                m.Dispose();
            foreach (Mat m in imageMats)
                m.Dispose();
            throw new UserFriendlyException("重新加载后有效内参照片不足，请重新拍摄");
        }

        using Mat cameraMatrix = new();
        using Mat distCoeffs = new();
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
        foreach (Mat m in rvecArray)
            m.Dispose();
        foreach (Mat m in tvecArray)
            m.Dispose();

        double maxReprojError = GetMaxSingleCameraReprojectionError(project);
        if (reprojError > maxReprojError)
        {
            throw new UserFriendlyException(
                $"内参重投影误差 {reprojError:F4} px，超过阈值 {maxReprojError:F2} px"
            );
        }

        string intrinsicJson = SerializeMatToJson(cameraMatrix);
        string distJson = SerializeVecToJson(distCoeffs);

        // 持久化：仅保存内参，清除外参字段
        IQueryable<CalibCameraParam> paramQuery = await _cameraParamRepo.GetQueryableAsync();
        CalibCameraParam? camParam = await AsyncExecuter.FirstOrDefaultAsync(
            paramQuery.Where(x =>
                x.CalibProjectId == calibProjectId && x.CameraDeviceId == cameraDeviceId
            )
        );
        if (camParam != null)
        {
            camParam.SetCalibResult(intrinsicJson, distJson, reprojError);
            await _cameraParamRepo.UpdateAsync(camParam);
        }
        else
        {
            // 尚未手动配置相机参数记录，自动创建最小记录以持久化内参结果
            CalibCameraParam newParam = new(
                GuidGenerator.Create(),
                calibProjectId,
                cameraDeviceId,
                "内参自动生成"
            );
            newParam.SetCalibResult(intrinsicJson, distJson, reprojError);
            await _cameraParamRepo.InsertAsync(newParam);
        }

        _logger.LogInformation("[内参标定] 完成，重投影误差 {Error:F4} px", reprojError);

        return new CalibComputeResultDto
        {
            IntrinsicMatrixJson = intrinsicJson,
            DistCoeffsJson = distJson,
            ReprojectionError = reprojError,
        };
    }

    /// <inheritdoc/>
    public async Task<CalibComputeResultDto> ComputeExtrinsicAsync(
        Guid calibProjectId,
        Guid cameraDeviceId
    )
    {
        _logger.LogInformation(
            "[外参标定] 开始计算 — 项目 {ProjectId} 相机 {CameraId}",
            calibProjectId,
            cameraDeviceId
        );

        CalibProject project = await _projectRepo.GetAsync(calibProjectId);

        // 加载已保存的相机内参（外参计算依赖内参）
        IQueryable<CalibCameraParam> paramQuery = await _cameraParamRepo.GetQueryableAsync();
        CalibCameraParam? camParam = await AsyncExecuter.FirstOrDefaultAsync(
            paramQuery.Where(x =>
                x.CalibProjectId == calibProjectId && x.CameraDeviceId == cameraDeviceId
            )
        );

        if (
            camParam == null
            || string.IsNullOrEmpty(camParam.IntrinsicMatrixJson)
            || string.IsNullOrEmpty(camParam.DistCoeffsJson)
        )
        {
            throw new UserFriendlyException("请先计算相机内参后再计算外参");
        }

        if (project.DeviceSeries != DeviceSeries.SingleLight)
        {
            throw new UserFriendlyException("无光系列不需要计算外参");
        }

        // 判断该相机是否需要计算外参（2目1光仅主相机计算外参）
        bool shouldComputeExtrinsic =
            project.DeviceType != CalibDeviceType.TwoCamera1Light
            || !project.MainCameraDeviceId.HasValue
            || project.MainCameraDeviceId.Value == cameraDeviceId;

        if (!shouldComputeExtrinsic)
        {
            throw new UserFriendlyException("当前相机为从相机（2目1光配置），无需计算外参");
        }

        // 从 DB 反序列化内参
        using Mat cameraMatrix = DeserializeMatrix(camParam.IntrinsicMatrixJson);
        using Mat distCoeffs = DeserializeVector(camParam.DistCoeffsJson);

        EnsureBoardConfigValid(project, isProjectedBoard: false);
        Size patternSize = GetBoardPatternSize(project, isProjectedBoard: false);
        float spacingMm = GetBoardSpacingMm(project);
        Point3f[] worldCorners = BuildBoardWorldPoints(project, patternSize, spacingMm);

        // 加载有效外参照片
        IQueryable<CalibPhotoRecord> query = await _photoRepo.GetQueryableAsync();
        List<CalibPhotoRecord> extrinsicPhotoRecords = await AsyncExecuter.ToListAsync(
            query
                .Where(x =>
                    x.CalibProjectId == calibProjectId
                    && x.CameraDeviceId == cameraDeviceId
                    && x.PhotoType == CalibPhotoType.Extrinsic
                )
                .OrderBy(x => x.CapturedAt)
        );

        List<ProjectorExtrinsicSampleGroup> extrinsicPhotos = BuildExtrinsicSampleGroups(
                extrinsicPhotoRecords
            )
            .Where(x => x.IsValid)
            .ToList();

        if (extrinsicPhotos.Count < CalibConsts.MinProjectorExtrinsicPhotoCount)
        {
            throw new UserFriendlyException(
                $"外参有效样本不足 {CalibConsts.MinProjectorExtrinsicPhotoCount} 组（当前 {extrinsicPhotos.Count} 组）"
            );
        }

        // SolvePnP：找最佳外参照片
        ProjectorExtrinsicSampleGroup? bestExtrinsic = await FindBestExtrinsicAsync(
            extrinsicPhotos,
            cameraMatrix,
            distCoeffs,
            patternSize,
            worldCorners,
            project
        );

        string? rvecJson = null;
        string? tvecJson = null;
        double? projectorReprojectionError = null;

        if (bestExtrinsic != null)
        {
            byte[] exBytes = await _blobContainer.GetAllBytesAsync(
                bestExtrinsic.ProjectorOffPhoto.BlobKey
            );
            using Mat exMat = LoadGrayMat(exBytes);
            Point2f[]? exCorners = FindBoardPointsSubpixGray(
                exMat,
                patternSize,
                GetDetectionBoardType(project, isProjectedBoard: false),
                project
            );
            if (exCorners != null)
            {
                using Mat rvec = new();
                using Mat tvec = new();
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
            }
        }

        if (
            projectorReprojectionError.HasValue
            && projectorReprojectionError.Value > CalibConsts.MaxSingleCameraReprojectionError
        )
        {
            throw new UserFriendlyException(
                $"外参重投影误差 {projectorReprojectionError.Value:F4} px，超过阈值，请补拍后重算"
            );
        }

        // 计算投影仪内参与相机-投影仪外参
        string? projIntrinsicJson = null;
        string? projDistJson = null;
        string? camToProjectorRJson = null;
        string? camToProjectorTJson = null;
        double? projCalibReprojError = null;

        EnsureBoardConfigValid(project, isProjectedBoard: true);
        Size projPatternSize = GetBoardPatternSize(project, isProjectedBoard: true);
        int projCols = projPatternSize.Width;
        int projRows = projPatternSize.Height;
        int projPx = Math.Max(project.ProjectedPixelSize, 1);

        Point2f[] projPixels = new Point2f[projCols * projRows];
        for (int r = 0; r < projRows; r++)
        for (int c = 0; c < projCols; c++)
            projPixels[r * projCols + c] = new Point2f((c + 1) * projPx, (r + 1) * projPx);

        Size projectorSize = new((projCols + 2) * projPx, (projRows + 2) * projPx);
        List<Mat> projObjMats = [];
        List<Mat> projImgMats = [];

        for (int ei = 0; ei < extrinsicPhotos.Count; ei++)
        {
            ProjectorExtrinsicSampleGroup sample = extrinsicPhotos[ei];
            try
            {
                Point3f[]? objPts = await BuildProjectorObjectPointsCamAsync(
                    sample,
                    cameraMatrix,
                    distCoeffs,
                    project,
                    patternSize,
                    worldCorners,
                    projPatternSize,
                    projPixels.Length
                );
                if (objPts == null)
                    continue;
                projObjMats.Add(Mat.FromArray(objPts));
                projImgMats.Add(Mat.FromArray(projPixels));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[外参标定] 外参照片[{Idx}]预处理失败，跳过", ei);
            }
        }

        if (projObjMats.Count >= CalibConsts.MinProjectorExtrinsicPhotoCount)
        {
            using Mat projKMat = new();
            using Mat projDistMat = new();
            Mat[] projRvecs = [];
            Mat[] projTvecs = [];
            try
            {
                projCalibReprojError = Cv2.CalibrateCamera(
                    projObjMats,
                    projImgMats,
                    projectorSize,
                    projKMat,
                    projDistMat,
                    out projRvecs,
                    out projTvecs,
                    CalibrationFlags.None
                );
                projIntrinsicJson = SerializeMatToJson(projKMat);
                projDistJson = SerializeVecToJson(projDistMat);

                int nPoses = projRvecs.Length;
                double[] sumR = new double[9];
                double[] sumT = new double[3];
                for (int pi = 0; pi < nPoses; pi++)
                {
                    using Mat R_proj_i = new();
                    Cv2.Rodrigues(projRvecs[pi], R_proj_i);
                    for (int rr = 0; rr < 3; rr++)
                    {
                        for (int cc = 0; cc < 3; cc++)
                            sumR[rr * 3 + cc] += R_proj_i.At<double>(rr, cc);
                        sumT[rr] += projTvecs[pi].At<double>(rr, 0);
                    }
                }
                using Mat R_cp_avg = new(3, 3, MatType.CV_64FC1);
                for (int rr = 0; rr < 3; rr++)
                for (int cc = 0; cc < 3; cc++)
                    R_cp_avg.Set(rr, cc, sumR[rr * 3 + cc] / nPoses);
                using Mat U_svd = new(),
                    S_svd = new(),
                    Vt_svd = new();
                Cv2.SVDecomp(R_cp_avg, S_svd, U_svd, Vt_svd, SVD.Flags.FullUV);
                using Mat R_cp_ortho = U_svd * Vt_svd;
                using Mat t_cp_avg = new(3, 1, MatType.CV_64FC1);
                for (int rr = 0; rr < 3; rr++)
                    t_cp_avg.Set(rr, 0, sumT[rr] / nPoses);
                camToProjectorRJson = SerializeMatToJson(R_cp_ortho);
                camToProjectorTJson = SerializeVecToJson(t_cp_avg);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[外参标定] 投影仪内参计算失败，跳过");
            }
            finally
            {
                foreach (Mat m in projRvecs)
                    m.Dispose();
                foreach (Mat m in projTvecs)
                    m.Dispose();
            }
        }

        foreach (Mat m in projObjMats)
            m.Dispose();
        foreach (Mat m in projImgMats)
            m.Dispose();

        // 持久化：保留已有内参，更新外参字段
        camParam.SetCalibResult(
            camParam.IntrinsicMatrixJson!,
            camParam.DistCoeffsJson!,
            camParam.ReprojectionError ?? 0,
            rvecJson,
            tvecJson,
            projectorReprojectionError,
            projIntrinsicJson,
            projDistJson,
            camToProjectorRJson,
            camToProjectorTJson,
            projCalibReprojError
        );
        await _cameraParamRepo.UpdateAsync(camParam);

        _logger.LogInformation(
            "[外参标定] 完成，外参误差 {Error}",
            projectorReprojectionError?.ToString("F4") ?? "—"
        );

        return new CalibComputeResultDto
        {
            IntrinsicMatrixJson = camParam.IntrinsicMatrixJson,
            DistCoeffsJson = camParam.DistCoeffsJson,
            ReprojectionError = camParam.ReprojectionError ?? 0,
            ExtrinsicRvecJson = rvecJson,
            ExtrinsicTvecJson = tvecJson,
            ProjectorReprojectionError = projectorReprojectionError,
            ProjectorIntrinsicMatrixJson = projIntrinsicJson,
            ProjectorDistCoeffsJson = projDistJson,
            CameraToProjectorRJson = camToProjectorRJson,
            CameraToProjectorTJson = camToProjectorTJson,
            ProjectorCalibReprojectionError = projCalibReprojError,
        };
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

        EnsureBoardConfigValid(project, isProjectedBoard: false);

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
            "[单目标定] 有效内参照片 {Count} 张，标定板 {Cols}x{Rows} 间距 {Size}mm",
            intrinsicPhotos.Count,
            GetBoardPatternSize(project, isProjectedBoard: false).Width,
            GetBoardPatternSize(project, isProjectedBoard: false).Height,
            GetBoardSpacingMm(project)
        );

        if (intrinsicPhotos.Count < CalibConsts.MinValidPhotoCount)
        {
            throw new UserFriendlyException(
                $"内参有效照片不足 {CalibConsts.MinValidPhotoCount} 张（当前 {intrinsicPhotos.Count} 张），无法计算内参"
            );
        }

        // 查询有效外参照片（无光系列不做外参计算）
        List<ProjectorExtrinsicSampleGroup> extrinsicPhotos = new();
        if (project.DeviceSeries == DeviceSeries.SingleLight)
        {
            List<CalibPhotoRecord> extrinsicPhotoRecords = await AsyncExecuter.ToListAsync(
                query
                    .Where(x =>
                        x.CalibProjectId == calibProjectId
                        && x.CameraDeviceId == cameraDeviceId
                        && x.PhotoType == CalibPhotoType.Extrinsic
                    )
                    .OrderBy(x => x.CapturedAt)
            );

            extrinsicPhotos = BuildExtrinsicSampleGroups(extrinsicPhotoRecords)
                .Where(x => x.IsValid)
                .ToList();
        }

        // ── 计算内参 ─────────────────────────────────────────────────────────────
        Size patternSize = GetBoardPatternSize(project, isProjectedBoard: false);
        float spacingMm = GetBoardSpacingMm(project);

        // 构建世界坐标（标定板平面，Z=0）
        Point3f[] worldCorners = BuildBoardWorldPoints(project, patternSize, spacingMm);

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
            Point2f[]? corners = FindBoardPointsSubpixGray(
                mat,
                patternSize,
                project.BoardType,
                project
            );
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
                $"投影外参有效样本不足 {CalibConsts.MinProjectorExtrinsicPhotoCount} 组（当前 {extrinsicPhotos.Count} 组），无法计算外参"
            );
        }

        if (shouldComputeProjectorExtrinsic && extrinsicPhotos.Count > 0)
        {
            _logger.LogInformation(
                "[单目标定] 开始 FindBestExtrinsic — {Count} 组外参样本",
                extrinsicPhotos.Count
            );
            phaseSw.Restart();
            ProjectorExtrinsicSampleGroup? bestExtrinsic = await FindBestExtrinsicAsync(
                extrinsicPhotos,
                cameraMatrix,
                distCoeffs,
                patternSize,
                worldCorners,
                project
            );
            _logger.LogInformation(
                "[单目标定] FindBestExtrinsic 耗时 {Ms}ms",
                phaseSw.ElapsedMilliseconds
            );

            if (bestExtrinsic != null)
            {
                byte[] exBytes = await _blobContainer.GetAllBytesAsync(
                    bestExtrinsic.ProjectorOffPhoto.BlobKey
                );
                using Mat exMat = LoadGrayMat(exBytes);
                Point2f[]? exCorners = FindBoardPointsSubpixGray(
                    exMat,
                    patternSize,
                    GetDetectionBoardType(project, isProjectedBoard: false),
                    project
                );
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

        // ── 计算投影仪内参与相机-投影仪外参 ───────────────────────────────────────────────
        string? projIntrinsicJson = null;
        string? projDistJson = null;
        string? camToProjectorRJson = null;
        string? camToProjectorTJson = null;
        double? projCalibReprojError = null;

        if (
            shouldComputeProjectorExtrinsic
            && extrinsicPhotos.Count >= CalibConsts.MinProjectorExtrinsicPhotoCount
        )
        {
            EnsureBoardConfigValid(project, isProjectedBoard: true);
            Size projPatternSize = GetBoardPatternSize(project, isProjectedBoard: true);
            int projCols = projPatternSize.Width;
            int projRows = projPatternSize.Height;
            int projPx = Math.Max(project.ProjectedPixelSize, 1);

            // 投影仪待标定的对应关系：
            //   对象点（Object Points）：投影图案角点在相机坐标系中的度量 3D 坐标，
            //     由白屏帧解算的实体标定板平面与图案帧相机光线求交得到（见 BuildProjectorObjectPointsCamAsync）。
            //   图像点（Image Points）：投影图案内角点在投影仪像素坐标中的已知位置。
            //   采用 calibrateCamera 拟合投影仪内参；由于对象点已在相机坐标系中，
            //   输出的 rvec/tvec 就是相机→投影仪的外参变换。

            // 投影仪像素坐标（已知，由投影图案决定）
            // 内角点布局：第 (c,r) 个角点在投影仪中为 ((c+1)*px, (r+1)*px)。
            Point2f[] projPixels = new Point2f[projCols * projRows];
            for (int r = 0; r < projRows; r++)
            for (int c = 0; c < projCols; c++)
                projPixels[r * projCols + c] = new Point2f((c + 1) * projPx, (r + 1) * projPx);

            // 投影仪分辨率估算（按图案大小，两边各留 1 格边距）
            Size projectorSize = new((projCols + 2) * projPx, (projRows + 2) * projPx);

            List<Mat> projObjMats = [];
            List<Mat> projImgMats = [];

            phaseSw.Restart();
            _logger.LogInformation(
                "[单目标定] 开始计算投影仪内参 — {Count} 组外参样本",
                extrinsicPhotos.Count
            );

            for (int ei = 0; ei < extrinsicPhotos.Count; ei++)
            {
                ProjectorExtrinsicSampleGroup sample = extrinsicPhotos[ei];
                try
                {
                    Point3f[]? objPts = await BuildProjectorObjectPointsCamAsync(
                        sample,
                        cameraMatrix,
                        distCoeffs,
                        project,
                        patternSize,
                        worldCorners,
                        projPatternSize,
                        projPixels.Length
                    );
                    if (objPts == null)
                        continue;

                    projObjMats.Add(Mat.FromArray(objPts));
                    projImgMats.Add(Mat.FromArray(projPixels));

                    _logger.LogDebug("[单目标定] 投影仪标定 外参样本[{Idx}] 光线-平面求交完成", ei);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[单目标定] 外参样本[{Idx}]投影仪预处理失败，跳过", ei);
                }
            }

            if (projObjMats.Count >= CalibConsts.MinProjectorExtrinsicPhotoCount)
            {
                using Mat projKMat = new();
                using Mat projDistMat = new();
                Mat[] projRvecs = [];
                Mat[] projTvecs = [];
                try
                {
                    projCalibReprojError = Cv2.CalibrateCamera(
                        projObjMats,
                        projImgMats,
                        projectorSize,
                        projKMat,
                        projDistMat,
                        out projRvecs,
                        out projTvecs,
                        CalibrationFlags.None
                    );

                    _logger.LogInformation(
                        "[单目标定] 投影仪内参计算完成，耐倦{Ms}ms，重投影误差 {Error:F4} px",
                        phaseSw.ElapsedMilliseconds,
                        projCalibReprojError
                    );

                    projIntrinsicJson = SerializeMatToJson(projKMat);
                    projDistJson = SerializeVecToJson(projDistMat);

                    // 相机→投影仪外参：对所有姿态的 R/t 为半正定矩阵平均
                    // 由于对象点已在相机坐标系中，
                    // calibrateCamera 的输出 rvec/tvec 直接是相机坐标系→投影仪的外参变换
                    int nPoses = projRvecs.Length;
                    double[] sumR = new double[9];
                    double[] sumT = new double[3];

                    for (int pi = 0; pi < nPoses; pi++)
                    {
                        using Mat R_proj_i = new();
                        Cv2.Rodrigues(projRvecs[pi], R_proj_i);
                        for (int rr = 0; rr < 3; rr++)
                        {
                            for (int cc = 0; cc < 3; cc++)
                                sumR[rr * 3 + cc] += R_proj_i.At<double>(rr, cc);
                            sumT[rr] += projTvecs[pi].At<double>(rr, 0);
                        }
                    }

                    // 构建平均 R 并通过 SVD 正交化
                    using Mat R_cp_avg = new(3, 3, MatType.CV_64FC1);
                    for (int rr = 0; rr < 3; rr++)
                    for (int cc = 0; cc < 3; cc++)
                        R_cp_avg.Set(rr, cc, sumR[rr * 3 + cc] / nPoses);

                    using Mat U_svd = new();
                    using Mat S_svd = new();
                    using Mat Vt_svd = new();
                    Cv2.SVDecomp(R_cp_avg, S_svd, U_svd, Vt_svd, SVD.Flags.FullUV);
                    using Mat R_cp_ortho = U_svd * Vt_svd;

                    using Mat t_cp_avg = new(3, 1, MatType.CV_64FC1);
                    for (int rr = 0; rr < 3; rr++)
                        t_cp_avg.Set(rr, 0, sumT[rr] / nPoses);

                    camToProjectorRJson = SerializeMatToJson(R_cp_ortho);
                    camToProjectorTJson = SerializeVecToJson(t_cp_avg);

                    _logger.LogInformation(
                        "[单目标定] 相机-投影仪外参计算完成，共 {Count} 帧平均",
                        nPoses
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[单目标定] 投影仪内参计算失败，跳过");
                    projCalibReprojError = null;
                }
                finally
                {
                    foreach (Mat m in projRvecs)
                        m.Dispose();
                    foreach (Mat m in projTvecs)
                        m.Dispose();
                }
            }
            else
            {
                _logger.LogWarning(
                    "[单目标定] 投影仪标定有效帧数不足（{Count}），跳过投影仪内参计算",
                    projObjMats.Count
                );
            }

            foreach (Mat m in projObjMats)
                m.Dispose();
            foreach (Mat m in projImgMats)
                m.Dispose();
        }

        double maxSingleReprojError = GetMaxSingleCameraReprojectionError(project);
        if (reprojError > maxSingleReprojError)
        {
            throw new UserFriendlyException(
                $"相机内参重投影误差为 {reprojError:F4} px，超过阈值 {maxSingleReprojError:F2} px，请补拍后重算"
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
                projectorReprojectionError,
                projIntrinsicJson,
                projDistJson,
                camToProjectorRJson,
                camToProjectorTJson,
                projCalibReprojError
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
            ProjectorIntrinsicMatrixJson = projIntrinsicJson,
            ProjectorDistCoeffsJson = projDistJson,
            CameraToProjectorRJson = camToProjectorRJson,
            CameraToProjectorTJson = camToProjectorTJson,
            ProjectorCalibReprojectionError = projCalibReprojError,
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
        List<ProjectorExtrinsicSampleGroup> extrinsicGroups = BuildExtrinsicSampleGroups(
            all.Where(x => x.PhotoType == CalibPhotoType.Extrinsic).ToList()
        );
        int extrinsicTotal = extrinsicGroups.Count;
        int extrinsicValid = extrinsicGroups.Count(x => x.IsValid);

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
                ProjectorIntrinsicMatrixJson = camParam.ProjectorIntrinsicMatrixJson,
                ProjectorDistCoeffsJson = camParam.ProjectorDistCoeffsJson,
                CameraToProjectorRJson = camParam.CameraToProjectorRJson,
                CameraToProjectorTJson = camParam.CameraToProjectorTJson,
                ProjectorCalibReprojectionError = camParam.ProjectorCalibReprojectionError,
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

        EnsureBoardConfigValid(project, isProjectedBoard: false);
        Size patternSize = GetBoardPatternSize(project, isProjectedBoard: false);
        float spacingMm = GetBoardSpacingMm(project);
        Point3f[] worldCorners = BuildBoardWorldPoints(project, patternSize, spacingMm);

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
            Point2f[]? mainCorners = FindBoardPointsSubpixGray(
                mainMat,
                patternSize,
                project.BoardType,
                project
            );
            Point2f[]? secondaryCorners = FindBoardPointsSubpixGray(
                secondaryMat,
                patternSize,
                project.BoardType,
                project
            );
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
    private static string BuildBlobKey(
        Guid projectId,
        Guid cameraId,
        CalibPhotoType type,
        ExtrinsicPhotoPhase? extrinsicPhase = null
    )
    {
        string typeStr = type switch
        {
            CalibPhotoType.Intrinsic => "intrinsic",
            CalibPhotoType.Extrinsic => "extrinsic",
            CalibPhotoType.StereoExtrinsicPair => "stereo-pair",
            _ => "unknown",
        };
        string ts = DateTime.UtcNow.ToString("yyyyMMddHHmmss_fff");
        string phaseSuffix = extrinsicPhase switch
        {
            ExtrinsicPhotoPhase.ProjectorOff => "_off",
            ExtrinsicPhotoPhase.ProjectorOn => "_on",
            _ => string.Empty,
        };
        return $"{projectId}/{cameraId}/{typeStr}/{ts}{phaseSuffix}.jpg";
    }

    private async Task DeletePhotoRecordsAsync(IEnumerable<CalibPhotoRecord> records)
    {
        List<CalibPhotoRecord> recordList = records.DistinctBy(x => x.Id).ToList();
        foreach (CalibPhotoRecord record in recordList)
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

        await _photoRepo.DeleteManyAsync(recordList.Select(x => x.Id));
    }

    private static List<ProjectorExtrinsicSampleGroup> BuildExtrinsicSampleGroups(
        IEnumerable<CalibPhotoRecord> photos
    )
    {
        return photos
            .Where(x => x.PairGroupId.HasValue)
            .GroupBy(x => x.PairGroupId!.Value)
            .Select(group =>
            {
                CalibPhotoRecord? projectorOff = group.FirstOrDefault(x =>
                    x.ExtrinsicPhase == ExtrinsicPhotoPhase.ProjectorOff
                );
                CalibPhotoRecord? projectorOn = group.FirstOrDefault(x =>
                    x.ExtrinsicPhase == ExtrinsicPhotoPhase.ProjectorOn
                );

                return projectorOff == null || projectorOn == null
                    ? null
                    : new ProjectorExtrinsicSampleGroup
                    {
                        PairGroupId = group.Key,
                        ProjectorOffPhoto = projectorOff,
                        ProjectorOnPhoto = projectorOn,
                    };
            })
            .Where(x => x != null)
            .Select(x => x!)
            .OrderBy(x => x.ProjectorOffPhoto.CapturedAt)
            .ToList();
    }

    private static CalibExtrinsicSampleDto ToExtrinsicSampleDto(
        Guid pairGroupId,
        CalibPhotoRecord projectorOffRecord,
        CalibPhotoRecord projectorOnRecord
    )
    {
        return new CalibExtrinsicSampleDto
        {
            PairGroupId = pairGroupId,
            ProjectorOffPhoto = ToPhotoDto(projectorOffRecord),
            ProjectorOnPhoto = ToPhotoDto(projectorOnRecord),
        };
    }

    private static CalibrationBoardType GetDetectionBoardType(
        CalibProject project,
        bool isProjectedBoard
    )
    {
        return isProjectedBoard ? CalibrationBoardType.Chessboard : project.BoardType;
    }

    /// <summary>标定板特征点检测入口（棋盘格/圆点板）。</summary>
    private (bool isValid, int cornerCount) DetectBoardFeaturePoints(
        byte[] imageBytes,
        CalibProject project,
        bool isProjectedBoard
    )
    {
        CalibrationBoardType detectionBoardType = GetDetectionBoardType(project, isProjectedBoard);
        if (detectionBoardType == CalibrationBoardType.Chessboard)
        {
            Size chess = GetBoardPatternSize(project, isProjectedBoard);

            // 投影棋盘格叠加在圆点物理板上时，暗色圆点会干扰棋盘格角点检测，
            // 先做形态学去点预处理再检测。
            if (isProjectedBoard && project.BoardType != CalibrationBoardType.Chessboard)
            {
                using Mat projGray = LoadGrayMat(imageBytes);
                if (projGray.Empty())
                    return (false, 0);
                using Mat cleaned = SuppressDotsForProjectedChessboard(projGray);
                Point2f[]? projCorners = FindCornersSubpixGray(cleaned, chess);
                if (projCorners == null)
                {
                    _logger.LogWarning(
                        "投影棋盘格检测失败(去点后): Pattern={Cols}x{Rows}, Image={W}x{H}",
                        chess.Width,
                        chess.Height,
                        projGray.Cols,
                        projGray.Rows
                    );
                }
                return projCorners == null ? (false, 0) : (true, projCorners.Length);
            }

            return DetectChessboardCorners(imageBytes, chess.Width, chess.Height);
        }

        try
        {
            Size patternSize = GetBoardPatternSize(project, isProjectedBoard);
            using Mat gray = LoadGrayMat(imageBytes);
            if (gray.Empty())
                return (false, 0);

            Point2f[]? points = FindBoardPointsSubpixGray(
                gray,
                patternSize,
                detectionBoardType,
                project
            );
            if (points == null)
            {
                _logger.LogWarning(
                    "圆点板检测失败: BoardType={BoardType}, Pattern={Cols}x{Rows}, Projected={Projected}, Image={W}x{H}",
                    detectionBoardType,
                    patternSize.Width,
                    patternSize.Height,
                    isProjectedBoard,
                    gray.Cols,
                    gray.Rows
                );
            }
            return points == null ? (false, 0) : (true, points.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "圆点板检测异常");
            return (false, 0);
        }
    }

    /// <summary>校验标定板参数是否合法。</summary>
    private static void EnsureBoardConfigValid(CalibProject project, bool isProjectedBoard)
    {
        if (isProjectedBoard)
        {
            if (project.ProjectedCornerRows < 2 || project.ProjectedCornerCols < 2)
            {
                throw new UserFriendlyException("请先配置投影棋盘格参数（内角点行数、列数）");
            }

            if (project.ProjectedPixelSize <= 0)
            {
                throw new UserFriendlyException("请先配置投影棋盘格像素尺寸");
            }

            return;
        }

        if (project.BoardType == CalibrationBoardType.Chessboard)
        {
            int rows = project.PhysicalCornerRows;
            int cols = project.PhysicalCornerCols;
            if (rows < 2 || cols < 2 || project.PhysicalSquareSizeMm <= 0)
            {
                throw new UserFriendlyException("请先配置棋盘格参数（内角点行数、列数、方格边长）");
            }

            return;
        }

        if (!project.CirclePatternRows.HasValue || !project.CirclePatternCols.HasValue)
            throw new UserFriendlyException("圆点板参数缺失：请设置行列数");
        if (!project.CircleSpacingMm.HasValue || project.CircleSpacingMm.Value <= 0)
            throw new UserFriendlyException("圆点板参数缺失：请设置圆点中心间距");
        if (project.CirclePatternRows.Value < 2 || project.CirclePatternCols.Value < 2)
            throw new UserFriendlyException("圆点板行列数至少为 2");

        if (project.BoardType == CalibrationBoardType.MarkedSymmetricCircleGrid)
        {
            if (project.MarkerRow is null || project.MarkerCol is null)
                throw new UserFriendlyException("中心标记圆点板缺少标记点坐标");

            if (
                project.MarkerRow.Value < 0
                || project.MarkerRow.Value >= project.CirclePatternRows.Value
                || project.MarkerCol.Value < 0
                || project.MarkerCol.Value >= project.CirclePatternCols.Value
            )
            {
                throw new UserFriendlyException("中心标记点坐标超出圆点板范围");
            }
        }
    }

    /// <summary>获取当前标定板模式下用于检测的行列尺寸。</summary>
    private static Size GetBoardPatternSize(CalibProject project, bool isProjectedBoard)
    {
        if (isProjectedBoard)
        {
            return new Size(project.ProjectedCornerCols, project.ProjectedCornerRows);
        }

        if (project.BoardType == CalibrationBoardType.Chessboard)
        {
            return new Size(project.PhysicalCornerCols, project.PhysicalCornerRows);
        }

        int cols = project.CirclePatternCols ?? 0;
        int rows = project.CirclePatternRows ?? 0;
        return new Size(cols, rows);
    }

    /// <summary>获取当前标定板物理间距（mm）。</summary>
    private static float GetBoardSpacingMm(CalibProject project)
    {
        return project.BoardType == CalibrationBoardType.Chessboard
            ? (float)project.PhysicalSquareSizeMm
            : (float)(project.CircleSpacingMm ?? 0m);
    }

    /// <summary>获取当前标定板类型对应的单目内参最大允许重投影误差（像素）。</summary>
    private static double GetMaxSingleCameraReprojectionError(CalibProject project)
    {
        return project.BoardType == CalibrationBoardType.Chessboard
            ? CalibConsts.MaxSingleCameraReprojectionError
            : CalibConsts.MaxCircleBoardReprojectionError;
    }

    /// <summary>构建标定板世界点。</summary>
    private static Point3f[] BuildBoardWorldPoints(
        CalibProject project,
        Size patternSize,
        float spacingMm
    )
    {
        bool asymmetric = project.BoardType == CalibrationBoardType.AsymmetricCircleGrid;
        bool marked = project.BoardType == CalibrationBoardType.MarkedSymmetricCircleGrid;
        int markerRow = project.MarkerRow ?? ((patternSize.Height - 1) / 2);
        int markerCol = project.MarkerCol ?? ((patternSize.Width - 1) / 2);

        // 中心标记圆点板按“缺孔板”处理：世界点中跳过缺失点，
        // 其余圆点保持标准 row-major 顺序。
        List<Point3f> points = new(patternSize.Width * patternSize.Height);
        for (int r = 0; r < patternSize.Height; r++)
        {
            for (int c = 0; c < patternSize.Width; c++)
            {
                if (marked && r == markerRow && c == markerCol)
                    continue;

                float x = asymmetric ? (2 * c + (r % 2)) * spacingMm : c * spacingMm;
                float y = r * spacingMm;
                points.Add(new Point3f(x, y, 0f));
            }
        }

        return points.ToArray();
    }

    /// <summary>根据标定板类型查找并返回有序特征点。</summary>
    private Point2f[]? FindBoardPointsSubpixGray(
        Mat grayFull,
        Size patternSize,
        CalibrationBoardType boardType,
        CalibProject? project = null
    )
    {
        if (boardType == CalibrationBoardType.Chessboard)
            return FindCornersSubpixGray(grayFull, patternSize);

        CircleBlobDetectorConfigDto detectorConfig = LoadCircleDetectorConfig(project);
        return FindCircleGridPoints(grayFull, patternSize, boardType, detectorConfig, project);
    }

    private static CircleBlobDetectorConfigDto LoadCircleDetectorConfig(CalibProject? project)
    {
        if (project == null || string.IsNullOrWhiteSpace(project.CircleDetectorConfigJson))
            return CircleBlobDetectorConfigDto.CreateDefault();

        try
        {
            return JsonSerializer.Deserialize<CircleBlobDetectorConfigDto>(
                    project.CircleDetectorConfigJson
                ) ?? CircleBlobDetectorConfigDto.CreateDefault();
        }
        catch
        {
            return CircleBlobDetectorConfigDto.CreateDefault();
        }
    }

    private Point2f[]? FindCircleGridPoints(
        Mat gray,
        Size patternSize,
        CalibrationBoardType boardType,
        CircleBlobDetectorConfigDto detectorConfig,
        CalibProject? project
    )
    {
        using Feature2D detector = CreateCircleBlobDetector(detectorConfig);

        if (boardType == CalibrationBoardType.MarkedSymmetricCircleGrid)
        {
            KeyPoint[] markedKeypoints = detector.Detect(gray);
            if (markedKeypoints.Length == 0)
            {
                _logger.LogWarning(
                    "中心缺孔圆点板检测失败: 未检测到任何 blob, Pattern={Cols}x{Rows}, Image={W}x{H}",
                    patternSize.Width,
                    patternSize.Height,
                    gray.Cols,
                    gray.Rows
                );
                return null;
            }

            Point2f[] markedCenters = markedKeypoints
                .Select(x => new Point2f(x.Pt.X, x.Pt.Y))
                .ToArray();

            int markerRow = project?.MarkerRow ?? ((patternSize.Height - 1) / 2);
            int markerCol = project?.MarkerCol ?? ((patternSize.Width - 1) / 2);

            bool markedOk = TryBuildOrderedCircleGrid(
                markedCenters,
                patternSize.Width,
                patternSize.Height,
                hasMarkerHole: true,
                markerRow,
                markerCol,
                out Point2f[]? orderedMarked,
                out string? markedFailureReason
            );
            if (!markedOk)
            {
                _logger.LogWarning(
                    "中心缺孔圆点板排序失败: BlobCount={BlobCount}, Expected={Expected}, Marker=({MarkerRow},{MarkerCol}), Reason={Reason}",
                    markedCenters.Length,
                    patternSize.Width * patternSize.Height - 1,
                    markerRow,
                    markerCol,
                    markedFailureReason ?? "unknown"
                );
            }

            return markedOk ? orderedMarked : null;
        }

        // 非缺孔板优先用 OpenCV 网格识别，它内部有成熟的网格拓扑排序，
        // 对完整规则网格最可靠，且能统一处理多角度倾斜拍摄。
        FindCirclesGridFlags flags =
            boardType == CalibrationBoardType.AsymmetricCircleGrid
                ? FindCirclesGridFlags.AsymmetricGrid | FindCirclesGridFlags.Clustering
                : FindCirclesGridFlags.SymmetricGrid | FindCirclesGridFlags.Clustering;

        if (Cv2.FindCirclesGrid(gray, patternSize, out Point2f[] centers, flags, detector))
            return centers;

        _logger.LogInformation(
            "OpenCV FindCirclesGrid 未识别成功，转入自定义排序兜底: BoardType={BoardType}, Pattern={Cols}x{Rows}, Flags={Flags}",
            boardType,
            patternSize.Width,
            patternSize.Height,
            flags
        );

        // OpenCV 网格识别失败时的兜底：用健壮的自定义排序（完整网格，不缺孔）。
        KeyPoint[] keypoints = detector.Detect(gray);
        if (keypoints.Length == 0)
        {
            _logger.LogWarning(
                "完整圆点板兜底检测失败: 未检测到任何 blob, BoardType={BoardType}, Pattern={Cols}x{Rows}",
                boardType,
                patternSize.Width,
                patternSize.Height
            );
            return null;
        }

        Point2f[] blobCenters = keypoints.Select(x => new Point2f(x.Pt.X, x.Pt.Y)).ToArray();

        bool orderedOk = TryBuildOrderedCircleGrid(
            blobCenters,
            patternSize.Width,
            patternSize.Height,
            hasMarkerHole: false,
            markerRow: 0,
            markerCol: 0,
            out Point2f[]? ordered,
            out string? failureReason
        );
        if (!orderedOk)
        {
            int fullExpected = patternSize.Width * patternSize.Height;
            if (blobCenters.Length == fullExpected - 1)
            {
                _logger.LogWarning(
                    "完整圆点板检测失败: 检测到 {BlobCount} 个圆点，恰好比完整网格 {Expected} 少 1 个，"
                        + "疑似为“中心缺孔板”。请将标定板类型改为“中心标记对称圆点网格”，"
                        + "并设置缺孔位置（{Cols}x{Rows} 板通常为 ({MarkerRow},{MarkerCol})）后重拍。",
                    blobCenters.Length,
                    fullExpected,
                    patternSize.Width,
                    patternSize.Height,
                    (patternSize.Height - 1) / 2,
                    (patternSize.Width - 1) / 2
                );
            }
            else
            {
                _logger.LogWarning(
                    "完整圆点板兜底排序失败: BlobCount={BlobCount}, Expected={Expected}, Reason={Reason}",
                    blobCenters.Length,
                    fullExpected,
                    failureReason ?? "unknown"
                );
            }
        }

        return orderedOk ? ordered : null;
    }

    private static Feature2D CreateCircleBlobDetector(CircleBlobDetectorConfigDto cfg)
    {
        var p = new SimpleBlobDetector.Params
        {
            MinThreshold = (float)cfg.MinThreshold,
            MaxThreshold = (float)cfg.MaxThreshold,
            FilterByArea = true,
            MinArea = (float)cfg.MinArea,
            MaxArea = (float)cfg.MaxArea,
            FilterByCircularity = true,
            MinCircularity = (float)cfg.MinCircularity,
            FilterByConvexity = true,
            MinConvexity = (float)cfg.MinConvexity,
            FilterByInertia = false,
        };
        return SimpleBlobDetector.Create(p);
    }

    private static bool TryBuildOrderedCircleGrid(
        Point2f[] points,
        int cols,
        int rows,
        bool hasMarkerHole,
        int markerRow,
        int markerCol,
        out Point2f[]? orderedPoints,
        out string? failureReason
    )
    {
        orderedPoints = null;
        failureReason = null;
        int expected = rows * cols - (hasMarkerHole ? 1 : 0);
        if (cols < 2 || rows < 2)
        {
            failureReason = $"invalid-grid:{cols}x{rows}";
            return false;
        }
        if (points.Length < expected)
        {
            failureReason = $"insufficient-points:{points.Length}<{expected}";
            return false;
        }

        // 平面标定板在透视投影下严格满足单应变换。用“四角单应 + 逐栅格最近点匹配”
        // 代替脆弱的全局 Y 间隙分行：后者在倾斜拍摄时远近行 Y 间距差异极大，会把大量点
        // 错分到同一行。单应方法对透视倾斜数学精确，并天然容忍缺孔与少量离群噪点。

        // 1. 用最近邻距离剔除离群噪点（安装孔、丝印等）：栅格点最近邻≈栅距，
        //    离群点最近邻远大于栅距。
        int n = points.Length;
        double[] nnDist = new double[n];
        for (int i = 0; i < n; i++)
        {
            double best = double.MaxValue;
            for (int j = 0; j < n; j++)
            {
                if (i == j)
                    continue;
                double dxi = points[i].X - points[j].X;
                double dyi = points[i].Y - points[j].Y;
                double d2 = (dxi * dxi) + (dyi * dyi);
                if (d2 < best)
                    best = d2;
            }
            nnDist[i] = Math.Sqrt(best);
        }
        double[] sortedNn = (double[])nnDist.Clone();
        Array.Sort(sortedNn);
        double medianNn = sortedNn[sortedNn.Length / 2];
        if (medianNn <= 1e-6)
        {
            failureReason = "degenerate-nn";
            return false;
        }

        List<Point2f> core = new(n);
        for (int i = 0; i < n; i++)
        {
            if (nnDist[i] <= 2.0 * medianNn)
                core.Add(points[i]);
        }
        if (core.Count < expected)
        {
            failureReason = $"core-insufficient:{core.Count}<{expected}";
            return false;
        }

        // 2. 用 x±y 极值确定四角（板近似轴对齐，允许透视倾斜）。
        Point2f tl = core[0],
            tr = core[0],
            br = core[0],
            bl = core[0];
        double tlv = double.MaxValue,
            brv = double.MinValue,
            trv = double.MinValue,
            blv = double.MaxValue;
        foreach (Point2f p in core)
        {
            double sum = p.X + p.Y;
            double diff = p.X - p.Y;
            if (sum < tlv)
            {
                tlv = sum;
                tl = p;
            }
            if (sum > brv)
            {
                brv = sum;
                br = p;
            }
            if (diff > trv)
            {
                trv = diff;
                tr = p;
            }
            if (diff < blv)
            {
                blv = diff;
                bl = p;
            }
        }

        // 3. 理想栅格四角 → 图像四角 的透视变换。
        Point2f[] idealCorners =
        [
            new(0, 0),
            new(cols - 1, 0),
            new(cols - 1, rows - 1),
            new(0, rows - 1),
        ];
        Point2f[] imageCorners = [tl, tr, br, bl];
        double[] h;
        try
        {
            using Mat hMat = Cv2.GetPerspectiveTransform(idealCorners, imageCorners);
            h = new double[9];
            for (int i = 0; i < 9; i++)
                h[i] = hMat.At<double>(i / 3, i % 3);
        }
        catch (Exception)
        {
            failureReason = "homography-failed";
            return false;
        }

        // 单元格尺寸（取栅格中心处，兼顾透视下的平均尺度）。
        int midC = cols / 2;
        int midR = rows / 2;
        Point2f pc = ApplyHomographyToPoint(h, midC, midR);
        Point2f pcx = ApplyHomographyToPoint(h, midC + 1, midR);
        Point2f pcy = ApplyHomographyToPoint(h, midC, midR + 1);
        double cell = Math.Min(PointDistanceForGrid(pc, pcx), PointDistanceForGrid(pc, pcy));
        if (cell <= 1e-3)
        {
            failureReason = "degenerate-cell";
            return false;
        }
        double half = 0.5 * cell;
        double matchThreshold2 = half * half;

        // 4. 逐栅格位置匹配最近的未用 core 点（跳过缺孔）。
        bool[] used = new bool[core.Count];
        Point2f[] ordered = new Point2f[expected];
        int outIdx = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                if (hasMarkerHole && r == markerRow && c == markerCol)
                    continue;

                Point2f pred = ApplyHomographyToPoint(h, c, r);
                int bestJ = -1;
                double bestD2 = matchThreshold2;
                for (int j = 0; j < core.Count; j++)
                {
                    if (used[j])
                        continue;
                    double dxj = core[j].X - pred.X;
                    double dyj = core[j].Y - pred.Y;
                    double d2 = (dxj * dxj) + (dyj * dyj);
                    if (d2 <= bestD2)
                    {
                        bestD2 = d2;
                        bestJ = j;
                    }
                }
                if (bestJ < 0)
                {
                    failureReason = $"cell-unmatched:({r},{c})";
                    return false;
                }
                used[bestJ] = true;
                ordered[outIdx++] = core[bestJ];
            }
        }

        if (outIdx != expected)
        {
            failureReason = $"ordered-count-mismatch:{outIdx}!={expected}";
            return false;
        }

        orderedPoints = ordered;
        return true;
    }

    private static Point2f ApplyHomographyToPoint(double[] h, double x, double y)
    {
        double w = (h[6] * x) + (h[7] * y) + h[8];
        if (Math.Abs(w) < 1e-12)
            w = w < 0 ? -1e-12 : 1e-12;
        double u = ((h[0] * x) + (h[1] * y) + h[2]) / w;
        double v = ((h[3] * x) + (h[4] * y) + h[5]) / w;
        return new Point2f((float)u, (float)v);
    }

    private static double PointDistanceForGrid(Point2f a, Point2f b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>
    /// 投影棋盘格叠加在圆点物理板上时，用灰度形态学闭运算抹除小于核尺寸的暗色圆点，
    /// 保留大尺度棋盘格结构，便于后续棋盘格角点检测。返回新建的灰度 Mat（调用方负责释放）。
    /// </summary>
    private static Mat SuppressDotsForProjectedChessboard(Mat gray)
    {
        int k = Math.Max(3, (int)Math.Round(gray.Width / 150.0));
        if (k % 2 == 0)
            k++;
        using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(k, k));
        Mat closed = new();
        Cv2.MorphologyEx(gray, closed, MorphTypes.Close, kernel);
        return closed;
    }

    /// <summary>OpenCV 棋盘格角点检测（使用亚像素精化）。</summary>
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

    /// <summary>
    /// 基于单组投影外参双拍样本，构建投影图案角点在相机坐标系中的度量 3D 坐标。
    /// 原理：白屏帧解算实体标定板在相机坐标系中的平面位姿；图案帧检测投影图案角点像素，
    /// 通过相机光线与标定板平面求交，得到投影角点的度量 3D 坐标（相机系）。
    /// 这样跨样本的对象点共享统一度量坐标系，投影仪内参与相机→投影仪外参才具有一致尺度。
    /// 任意一步检测失败或几何异常时返回 null，由调用方跳过该样本。
    /// </summary>
    private async Task<Point3f[]?> BuildProjectorObjectPointsCamAsync(
        ProjectorExtrinsicSampleGroup sample,
        Mat cameraMatrix,
        Mat distCoeffs,
        CalibProject project,
        Size physicalPatternSize,
        Point3f[] physicalWorldCorners,
        Size projPatternSize,
        int expectedProjCornerCount
    )
    {
        // 1. 白屏帧 → 实体标定板在相机坐标系中的平面位姿
        byte[] offBytes = await _blobContainer.GetAllBytesAsync(sample.ProjectorOffPhoto.BlobKey);
        using Mat offGray = LoadGrayMat(offBytes);
        if (offGray.Empty())
            return null;
        Point2f[]? boardCorners = FindBoardPointsSubpixGray(
            offGray,
            physicalPatternSize,
            GetDetectionBoardType(project, isProjectedBoard: false),
            project
        );
        if (boardCorners == null || boardCorners.Length != physicalWorldCorners.Length)
            return null;

        using Mat rvecBoard = new();
        using Mat tvecBoard = new();
        Cv2.SolvePnP(
            InputArray.Create(physicalWorldCorners),
            InputArray.Create(boardCorners),
            cameraMatrix,
            distCoeffs,
            rvecBoard,
            tvecBoard
        );
        using Mat rBoard = new();
        Cv2.Rodrigues(rvecBoard, rBoard);

        // 标定板平面：法向量 = 板坐标 Z 轴在相机系（R 第三列）；平面上一点 = 板原点在相机系 = t
        double nx = rBoard.At<double>(0, 2);
        double ny = rBoard.At<double>(1, 2);
        double nz = rBoard.At<double>(2, 2);
        double p0x = tvecBoard.At<double>(0, 0);
        double p0y = tvecBoard.At<double>(1, 0);
        double p0z = tvecBoard.At<double>(2, 0);
        double nDotP0 = (nx * p0x) + (ny * p0y) + (nz * p0z);

        // 2. 图案帧 → 投影图案角点像素（圆点物理板上先去点再检测棋盘格）
        byte[] onBytes = await _blobContainer.GetAllBytesAsync(sample.ProjectorOnPhoto.BlobKey);
        using Mat onGray = LoadGrayMat(onBytes);
        if (onGray.Empty())
            return null;

        Point2f[]? patternCorners;
        if (project.BoardType == CalibrationBoardType.Chessboard)
        {
            patternCorners = FindBoardPointsSubpixGray(
                onGray,
                projPatternSize,
                GetDetectionBoardType(project, isProjectedBoard: true),
                project
            );
        }
        else
        {
            using Mat cleaned = SuppressDotsForProjectedChessboard(onGray);
            patternCorners = FindBoardPointsSubpixGray(
                cleaned,
                projPatternSize,
                GetDetectionBoardType(project, isProjectedBoard: true),
                project
            );
        }
        if (patternCorners == null || patternCorners.Length != expectedProjCornerCount)
            return null;

        // 3. 去畸变到归一化相机坐标（作为光线方向），与标定板平面求交得到度量 3D
        using Mat undistMat = new();
        Cv2.UndistortPoints(InputArray.Create(patternCorners), undistMat, cameraMatrix, distCoeffs);
        undistMat.GetArray(out Point2f[] undist);
        if (undist.Length != patternCorners.Length)
            return null;

        Point3f[] objPts = new Point3f[patternCorners.Length];
        for (int i = 0; i < patternCorners.Length; i++)
        {
            double dx = undist[i].X;
            double dy = undist[i].Y;
            const double dz = 1.0;
            double nDotD = (nx * dx) + (ny * dy) + (nz * dz);
            if (Math.Abs(nDotD) < 1e-9)
                return null;
            double tRay = nDotP0 / nDotD;
            if (tRay <= 0)
                return null;
            objPts[i] = new Point3f((float)(tRay * dx), (float)(tRay * dy), (float)(tRay * dz));
        }

        return objPts;
    }

    /// <summary>从多组外参样本中找重投影误差最小的一组（以白屏帧实体板位姿为准）</summary>
    private async Task<ProjectorExtrinsicSampleGroup?> FindBestExtrinsicAsync(
        List<ProjectorExtrinsicSampleGroup> photos,
        Mat cameraMatrix,
        Mat distCoeffs,
        Size patternSize,
        Point3f[] worldCorners,
        CalibProject project
    )
    {
        ProjectorExtrinsicSampleGroup? best = null;
        double bestError = double.MaxValue;

        foreach (ProjectorExtrinsicSampleGroup sample in photos)
        {
            try
            {
                byte[] bytes = await _blobContainer.GetAllBytesAsync(
                    sample.ProjectorOffPhoto.BlobKey
                );

                // 灰度加载 + 半分辨率加速角点检测
                using Mat mat = LoadGrayMat(bytes);
                if (mat.Empty())
                    continue;

                Point2f[]? corners = FindBoardPointsSubpixGray(
                    mat,
                    patternSize,
                    GetDetectionBoardType(project, isProjectedBoard: false),
                    project
                );
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
                    best = sample;
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
            BoardType = project.BoardType,
            PhysicalCornerRows = project.PhysicalCornerRows,
            PhysicalCornerCols = project.PhysicalCornerCols,
            PhysicalSquareSizeMm = project.PhysicalSquareSizeMm,
            ProjectedCornerRows = project.ProjectedCornerRows,
            ProjectedCornerCols = project.ProjectedCornerCols,
            ProjectedPixelSize = project.ProjectedPixelSize,
            CircleBoardConfig =
                project.BoardType == CalibrationBoardType.Chessboard
                    ? null
                    : new CircleBoardConfigDto
                    {
                        PatternSize = new CirclePatternSizeDto
                        {
                            Width = project.CirclePatternCols ?? 27,
                            Height = project.CirclePatternRows ?? 27,
                        },
                        CircleSpacing = project.CircleSpacingMm ?? 10m,
                        CircleDiameter = project.CircleDiameterMm,
                        HasCenterMarker = project.HasCenterMarker ?? false,
                        HasCornerLocators = project.HasCornerLocators ?? false,
                        MarkerPosition = new CircleMarkerPositionDto
                        {
                            Row = project.MarkerRow ?? 13,
                            Col = project.MarkerCol ?? 13,
                        },
                        Detector = LoadCircleDetectorConfig(project),
                    },
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
            ExtrinsicPhase = record.ExtrinsicPhase switch
            {
                ExtrinsicPhotoPhase.ProjectorOff => ExtrinsicPhotoPhaseDto.ProjectorOff,
                ExtrinsicPhotoPhase.ProjectorOn => ExtrinsicPhotoPhaseDto.ProjectorOn,
                _ => null,
            },
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

    /// <inheritdoc/>
    public async Task<bool> ValidateStep5Async(Guid calibProjectId)
    {
        CalibProject project = await _projectRepo.GetAsync(calibProjectId);

        // 收集所有绑定相机 ID
        List<Guid> cameraIds = new();
        if (project.MainCameraDeviceId.HasValue)
            cameraIds.Add(project.MainCameraDeviceId.Value);
        if (project.SecondaryCameraDeviceId.HasValue)
            cameraIds.Add(project.SecondaryCameraDeviceId.Value);
        if (cameraIds.Count == 0)
            return false;

        IQueryable<CalibCameraParam> paramQuery = await _cameraParamRepo.GetQueryableAsync();
        List<CalibCameraParam> camParams = await AsyncExecuter.ToListAsync(
            paramQuery.Where(x =>
                x.CalibProjectId == calibProjectId && cameraIds.Contains(x.CameraDeviceId)
            )
        );

        // 所有相机的内参必须已计算
        bool allIntrinsicDone = cameraIds.All(id =>
            camParams.Any(p =>
                p.CameraDeviceId == id && !string.IsNullOrEmpty(p.IntrinsicMatrixJson)
            )
        );
        if (!allIntrinsicDone)
            return false;

        // 单光系列：主相机的外参（相机→投影棋盘格位姿）也必须已计算
        if (project.DeviceSeries == DeviceSeries.SingleLight)
        {
            if (!project.MainCameraDeviceId.HasValue)
                return false;
            bool extrinsicDone = camParams.Any(p =>
                p.CameraDeviceId == project.MainCameraDeviceId.Value
                && !string.IsNullOrEmpty(p.ExtrinsicRvecJson)
            );
            if (!extrinsicDone)
                return false;
        }

        // 双目项目：双目外参也必须已计算
        if (project.SecondaryCameraDeviceId.HasValue)
        {
            IQueryable<CalibStereoResult> stereoQuery = await _stereoResultRepo.GetQueryableAsync();
            bool stereoExists = await AsyncExecuter.AnyAsync(
                stereoQuery.Where(x => x.CalibProjectId == calibProjectId)
            );
            if (!stereoExists)
                return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public async Task<AutoAlignCamerasResultDto> AutoAlignCamerasAsync(Guid id)
    {
        CalibProject project = await _projectRepo.GetAsync(id);

        // 确保投影仪已绑定
        Guid? projectorId = project.BoundProjectorDeviceId;

        // 投影十字架（有投影仪时操作，无投影仪时仍拍照但不投影）
        if (projectorId.HasValue)
        {
            try
            {
                // 开灯
                await _projectorService.LedOnAsync(projectorId.Value);
                // 切换为十字架显示模式
                await _projectorService.SetDisplayModeAsync(
                    new SetProjectorDisplayModeDto
                    {
                        ProjectorDeviceId = projectorId.Value,
                        Mode = ProjectorDisplayMode.Cross,
                    }
                );
                // 等待投影稳定
                await Task.Delay(300);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AutoAlign] 投影十字架失败，跳过投影步骤");
            }
        }

        // 执行主从相机对齐
        CameraAlignCameraResult? mainResult = null;
        CameraAlignCameraResult? secondaryResult = null;

        if (project.MainCameraDeviceId.HasValue)
        {
            mainResult = await AlignOneCameraAsync(
                project,
                project.MainCameraDeviceId.Value,
                project.MainCameraMotorAxisId,
                "主相机"
            );
        }

        if (project.SecondaryCameraDeviceId.HasValue)
        {
            secondaryResult = await AlignOneCameraAsync(
                project,
                project.SecondaryCameraDeviceId.Value,
                project.SecondaryCameraMotorAxisId,
                "从相机"
            );
        }

        // 执行完毕后关灯（恢复投影仪状态）
        if (projectorId.HasValue)
        {
            try
            {
                await _projectorService.LedOffAsync(projectorId.Value);
            }
            catch
            { /* 关灯失败不影响结果 */
            }
        }

        bool success =
            (mainResult == null || mainResult.Skipped || mainResult.IsAligned)
            && (secondaryResult == null || secondaryResult.Skipped || secondaryResult.IsAligned);

        return new AutoAlignCamerasResultDto
        {
            MainCamera = mainResult,
            SecondaryCamera = secondaryResult,
            Success = success,
            Message = success ? "相机对齐完成" : "部分相机未能完成对齐，请检查结果详情",
        };
    }

    /// <summary>
    /// 对单台相机执行十字架检测 + 电机调整：
    ///   拍照 → OpenCV 检测十字架中心 → 计算偏差角度 → 移动电机。
    ///
    /// 运动方向规则（由 Step 4 伺服参数 PositiveSoftLimit / NegativeSoftLimit 决定）：
    ///   情况A（configMin ≤ configMax）：从最小到最大为正向运动，十字从图像右侧向左侧移动。
    ///     → 移到0点时反向（-），扫描时正向（+），微调偏移符号不变。
    ///   情况B（configMin > configMax）：从最小到最大为反向运动，十字从图像左侧向右侧移动。
    ///     → 移到0点时正向（+），扫描时反向（-），微调偏移符号取反。
    ///
    /// 注意：镜头朝正下方为 0°（单圈位置 0 LSB）。
    /// </summary>
    private async Task<CameraAlignCameraResult> AlignOneCameraAsync(
        CalibProject project,
        Guid cameraDeviceId,
        Guid? motorAxisId,
        string cameraRole
    )
    {
        // 如果未绑定电机，跳过（无法调整）
        if (!motorAxisId.HasValue)
        {
            return new CameraAlignCameraResult
            {
                CameraDeviceId = cameraDeviceId,
                CameraRole = cameraRole,
                Skipped = true,
                Message = "未绑定角度控制电机，跳过",
            };
        }

        // 优先读取电机轴配置
        MotorAxis axis = await _motorAxisRepository.GetAsync(motorAxisId.Value);
        if (axis.Brand != MotorBrand.KtechKtech)
        {
            return new CameraAlignCameraResult
            {
                CameraDeviceId = cameraDeviceId,
                CameraRole = cameraRole,
                Message = $"电机品牌 {axis.Brand} 暂不支持自动对齐，仅支持瓴控 KTECH",
            };
        }

        KtechMotorDriver? driver = _motorControlService.GetKtechMotorDriver(axis.SlaveId);
        if (driver == null)
        {
            return new CameraAlignCameraResult
            {
                CameraDeviceId = cameraDeviceId,
                CameraRole = cameraRole,
                Message = $"电机驱动未注册（SlaveId={axis.SlaveId}），请确认设备已连接",
            };
        }

        // 读取焦距（用于角度计算）
        double? focalLengthPx = await GetFocalLengthPixelsAsync(project.Id, cameraDeviceId);
        if (!focalLengthPx.HasValue || focalLengthPx.Value <= 0)
        {
            return new CameraAlignCameraResult
            {
                CameraDeviceId = cameraDeviceId,
                CameraRole = cameraRole,
                Message = "未找到相机内参（焦距），无法计算目标角度，请先完成内参标定",
            };
        }

        // ── 读取 Step 4 伺服参数配置的行程限位 ──
        // PositiveSoftLimit = 电机行程最大位置，NegativeSoftLimit = 电机最小位置
        // 单位：CalibMotorParam 存储角度（°），KTECH LSB = 0.01°，故乘以 100
        IQueryable<CalibMotorParam> mpQ = await _motorParamRepo.GetQueryableAsync();
        CalibMotorParam? motorParam = await AsyncExecuter.FirstOrDefaultAsync(
            mpQ.Where(x => x.CalibProjectId == project.Id && x.MotorAxisId == motorAxisId.Value)
        );
        long configMinLsb = motorParam != null ? (long)(motorParam.NegativeSoftLimit * 100m) : 0;
        long configMaxLsb =
            motorParam != null ? (long)(motorParam.PositiveSoftLimit * 100m) : 35999;

        // 情况A：configMin ≤ configMax → 正向运动范围，十字右→左
        // 情况B：configMin > configMax → 反向运动范围，十字左→右
        bool isCaseA = configMinLsb <= configMaxLsb;

        // 扫描时边界（情况A为最大值，情况B为最小值）
        long scanBoundaryLsb = isCaseA ? configMaxLsb : configMinLsb;

        _logger.LogInformation(
            "[AutoAlign] {Role} 行程参数：configMin={Min} LSB, configMax={Max} LSB, 情况{Case}",
            cameraRole,
            configMinLsb,
            configMaxLsb,
            isCaseA ? "A（正向范围）" : "B（反向范围）"
        );

        const uint MoveSpeedCentidps = 18000;
        const long HalfCircleLsb = 18000; // 180° = 18000 LSB

        // ── Step 1：读取调整前单圈角度 ──
        uint singleBefore = await driver.ReadSingleAngleAsync();
        double angleBefore = singleBefore / 100.0;
        long currLsb = (long)singleBefore;

        // ── Step 2：移动到单圈 0 点（镜头朝正下方） ──
        // 策略：取圆周最短路径，避免电机多转
        //   当前角度 ≤ 180°（≤ 18000 LSB）→ CCW 反向（dir=1）到 0° 最近
        //   当前角度 > 180°（> 18000 LSB）→ CW 正向（dir=0）到 0° 最近
        if (currLsb > 0)
        {
            byte dirToZero = currLsb <= HalfCircleLsb ? (byte)1 : (byte)0;
            _logger.LogInformation(
                "[AutoAlign] {Role} 移到0点：currLsb={Curr} LSB（{Deg:F2}°），dir={Dir}（{Desc}）",
                cameraRole,
                currLsb,
                currLsb / 100.0,
                dirToZero,
                dirToZero == 1 ? "CCW 反向" : "CW 正向"
            );
            await driver.SingleAngleWithSpeedAsync(dirToZero, 0u, MoveSpeedCentidps);
            bool reachedZero = await WaitUntilSingleAngleReachedAsync(driver, 0L, 30);
            if (!reachedZero)
            {
                return new CameraAlignCameraResult
                {
                    CameraDeviceId = cameraDeviceId,
                    CameraRole = cameraRole,
                    AngleBeforeDeg = angleBefore,
                    Message = "电机移到0点超时（30s），请检查机械状态或限位设置",
                };
            }
            currLsb = 0;
        }

        // ── Step 3：从 0 点起扫描，最多 10 步（每步 5°） ──
        // 情况A：相对 +运动（正向）直到 configMaxLsb
        // 情况B：相对 -运动（反向）直到 configMinLsb（注意此时 configMinLsb > configMaxLsb）
        const long ScanStepLsb = 500; // 5° = 500 × 0.01°
        const int MaxScanSteps = 10;
        byte[] imageBytes = null!;
        Point2d? crossCenter = null;

        for (int step = 0; step <= MaxScanSteps; step++)
        {
            try
            {
                imageBytes = await GrabCalibFrameRawAsync(cameraDeviceId, 0);
            }
            catch (Exception ex)
            {
                return new CameraAlignCameraResult
                {
                    CameraDeviceId = cameraDeviceId,
                    CameraRole = cameraRole,
                    AngleBeforeDeg = angleBefore,
                    Message = $"拍照失败：{ex.Message}",
                };
            }

            crossCenter = DetectCrossCenter(imageBytes);
            if (crossCenter.HasValue)
                break; // 已找到十字，退出扫描

            if (step == MaxScanSteps)
                break; // 达到最大步数

            // 计算下一步绝对目标角度（使用单圈绝对定位命令，避免增量累计误差）
            long nextTargetLsb;
            byte scanDir;
            bool exceedsBoundary;

            if (isCaseA)
            {
                // 情况A：从 0° 正向扫描（角度递增），dir=0（CW）
                nextTargetLsb = (long)(step + 1) * ScanStepLsb;
                exceedsBoundary = nextTargetLsb > configMaxLsb;
                scanDir = 0; // CW 正向
            }
            else
            {
                // 情况B：从 0° 反向扫描（角度递减，绕圈：35500、35000...），dir=1（CCW）
                long offset = (long)(step + 1) * ScanStepLsb;
                nextTargetLsb = (36000L - offset % 36000L) % 36000L;
                exceedsBoundary = nextTargetLsb < configMinLsb;
                scanDir = 1; // CCW 反向
            }

            if (exceedsBoundary)
            {
                // 超出行程边界 → 先归零，再返回未找到
                byte dirBack = currLsb <= HalfCircleLsb ? (byte)1 : (byte)0;
                _logger.LogInformation(
                    "[AutoAlign] {Role} 超出行程边界，移回0点：currLsb={Curr} LSB，dir={Dir}",
                    cameraRole,
                    currLsb,
                    dirBack
                );
                await driver.SingleAngleWithSpeedAsync(dirBack, 0u, MoveSpeedCentidps);
                await WaitUntilSingleAngleReachedAsync(driver, 0L, 30);

                return new CameraAlignCameraResult
                {
                    CameraDeviceId = cameraDeviceId,
                    CameraRole = cameraRole,
                    AngleBeforeDeg = angleBefore,
                    Message =
                        $"扫描至行程边界 {scanBoundaryLsb / 100.0:F2}°，未检测到十字架，电机已归零，请确认投影仪状态",
                };
            }

            _logger.LogInformation(
                "[AutoAlign] {Role} 扫描 step={Step}，目标 {Target} LSB（{Deg:F2}°），dir={Dir}",
                cameraRole,
                step + 1,
                nextTargetLsb,
                nextTargetLsb / 100.0,
                scanDir
            );
            await driver.SingleAngleWithSpeedAsync(scanDir, (uint)nextTargetLsb, MoveSpeedCentidps);
            bool scanMoved = await WaitUntilSingleAngleReachedAsync(driver, nextTargetLsb, 10);
            if (!scanMoved)
            {
                return new CameraAlignCameraResult
                {
                    CameraDeviceId = cameraDeviceId,
                    CameraRole = cameraRole,
                    AngleBeforeDeg = angleBefore,
                    Message =
                        $"扫描步进超时（10s），目标 {nextTargetLsb / 100.0:F2}°，请检查机械状态",
                };
            }
            currLsb = nextTargetLsb;
        }

        if (!crossCenter.HasValue)
        {
            // 扫描结束仍未找到十字 → 先归零，再返回未找到
            byte dirBack = currLsb <= HalfCircleLsb ? (byte)1 : (byte)0;
            _logger.LogInformation(
                "[AutoAlign] {Role} 扫描结束未找到十字，移回0点：currLsb={Curr} LSB，dir={Dir}",
                cameraRole,
                currLsb,
                dirBack
            );
            await driver.SingleAngleWithSpeedAsync(dirBack, 0u, MoveSpeedCentidps);
            await WaitUntilSingleAngleReachedAsync(driver, 0L, 30);

            return new CameraAlignCameraResult
            {
                CameraDeviceId = cameraDeviceId,
                CameraRole = cameraRole,
                AngleBeforeDeg = angleBefore,
                Message =
                    $"扫描 {MaxScanSteps} 步（{MaxScanSteps * 5}°）未检测到十字架，电机已归零，请确认投影仪状态",
            };
        }

        // ── Step 4：视野内已有十字，闭环微调（最多 10 次迭代） ──
        // 进入微调前，重新读取实际单圈角度（扫描阶段若 step=0 即找到十字，currLsb 仍为归零后的 0，
        // 必须以硬件实际角度为基准，否则 Clamp 计算会产生巨大偏差）
        currLsb = (long)await driver.ReadSingleAngleAsync();
        _logger.LogInformation(
            "[AutoAlign] {Role} 进入微调，实际单圈角度={Curr} LSB（{Deg:F2}°）",
            cameraRole,
            currLsb,
            currLsb / 100.0
        );
        const int MaxIterations = 10;
        const double AlignThresholdPixels = 10.0;

        double totalAdjustDeg = 0;
        double offsetXPixels = 0;
        double offsetYPixels = 0;
        int imgCols = 0;
        int imgRows = 0;

        for (int iter = 0; iter < MaxIterations; iter++)
        {
            using Mat iterImg = LoadGrayMat(imageBytes);
            imgCols = iterImg.Cols;
            imgRows = iterImg.Rows;
            offsetXPixels = crossCenter!.Value.X - imgCols / 2.0;
            offsetYPixels = crossCenter.Value.Y - imgRows / 2.0;

            if (Math.Abs(offsetXPixels) <= AlignThresholdPixels)
                break; // 已对齐，退出

            // θ = arctan(offset_px / focal_px)
            // 情况A：偏右(offsetX>0)→ 正向(CW)旋转使十字左移；偏左(offsetX<0)→ 反向(CCW)旋转
            // 情况B：偏左(offsetX<0)→ 继续反向(CCW)旋转使十字右移；偏右(offsetX>0)→ 跳过了，正向(CW)旋转
            // 两种情况下 adjustDeg 符号与 offsetX 相同，无需取反
            double rawAdjustDeg = Math.Atan2(offsetXPixels, focalLengthPx.Value) * 180.0 / Math.PI;
            double adjustDeg = rawAdjustDeg;

            // 计算目标角度（单圈绝对位置），按行程范围 Clamp
            // 情况A：Clamp 到 [configMinLsb, configMaxLsb]
            // 情况B：当前位于 [configMinLsb, 35999] 区间，Clamp 到同一范围
            long clampLo = configMinLsb;
            long clampHi = isCaseA ? configMaxLsb : 35999L;
            long targetLsb = Math.Clamp(
                currLsb + (long)Math.Round(adjustDeg * 100),
                clampLo,
                clampHi
            );
            int deltaLsb = (int)(targetLsb - currLsb);
            if (deltaLsb == 0)
                break; // 已在边界，无法继续调整

            // 方向由增量符号决定：正增量=CW（dir=0），负增量=CCW（dir=1）
            byte dirAdjust = deltaLsb > 0 ? (byte)0 : (byte)1;
            totalAdjustDeg += deltaLsb / 100.0;

            _logger.LogInformation(
                "[AutoAlign] {Role} 微调 iter={Iter}，offsetX={OffX:F1}px，delta={Delta} LSB（{Deg:F2}°），dir={Dir}",
                cameraRole,
                iter,
                offsetXPixels,
                deltaLsb,
                deltaLsb / 100.0,
                dirAdjust
            );
            await driver.SingleAngleWithSpeedAsync(dirAdjust, (uint)targetLsb, MoveSpeedCentidps);
            await WaitUntilSingleAngleReachedAsync(driver, targetLsb, 30);
            currLsb = targetLsb;

            if (iter < MaxIterations - 1)
            {
                try
                {
                    imageBytes = await GrabCalibFrameRawAsync(cameraDeviceId, 0);
                }
                catch
                {
                    break;
                }
                crossCenter = DetectCrossCenter(imageBytes);
                if (!crossCenter.HasValue)
                    break;
            }
        }

        uint singleAfter = await driver.ReadSingleAngleAsync();
        bool isAligned = Math.Abs(offsetXPixels) <= AlignThresholdPixels;
        string msg = isAligned
            ? $"偏差 {offsetXPixels:F1} px，已对齐（总调整 {totalAdjustDeg:F2}°）"
            : $"偏差 {offsetXPixels:F1} px，迭代 {MaxIterations} 次未完全对齐（总调整 {totalAdjustDeg:F2}°）";

        return new CameraAlignCameraResult
        {
            CameraDeviceId = cameraDeviceId,
            CameraRole = cameraRole,
            CrossOffsetXPixels = offsetXPixels,
            CrossOffsetYPixels = offsetYPixels,
            AngleBeforeDeg = angleBefore,
            AngleAfterDeg = singleAfter / 100.0,
            AdjustedAngleDeg = totalAdjustDeg,
            IsAligned = isAligned,
            Message = msg,
        };
    }

    /// <summary>
    /// 检测投影仪十字图（白底暗线）的交叉中心坐标。
    /// 策略：行/列投影均值 → 找亮度最低的列（垂直线）和行（水平线）→ 加权重心提升精度。
    /// </summary>
    private static Point2d? DetectCrossCenter(byte[] imageBytes)
    {
        try
        {
            using Mat gray = LoadGrayMat(imageBytes);
            if (gray.Empty())
                return null;

            // 高斯模糊，削弱噪点对投影的影响
            using Mat blurred = new();
            Cv2.GaussianBlur(gray, blurred, new Size(9, 9), 2.0);

            int rows = blurred.Rows;
            int cols = blurred.Cols;

            // 排除图像边缘 10%（摄像头暗角/遮挡物干扰）
            int marginX = cols / 10;
            int marginY = rows / 10;
            OpenCvSharp.Range rowRange = new(marginY, rows - marginY);
            OpenCvSharp.Range colRange = new(marginX, cols - marginX);
            using Mat roi = blurred[rowRange, colRange];

            // 列投影：沿行方向求均值，得到 1×roiCols 行向量，找垂直暗线
            using Mat colMeans = new();
            Cv2.Reduce(roi, colMeans, ReduceDimension.Row, ReduceTypes.Avg, MatType.CV_32F);

            // 行投影：沿列方向求均值，得到 roiRows×1 列向量，找水平暗线
            using Mat rowMeans = new();
            Cv2.Reduce(roi, rowMeans, ReduceDimension.Column, ReduceTypes.Avg, MatType.CV_32F);

            // 找亮度最小点
            Cv2.MinMaxLoc(colMeans, out _, out _, out Point minColPt, out _);
            Cv2.MinMaxLoc(rowMeans, out _, out _, out Point minRowPt, out _);

            // 在最小值邻域内做加权重心（暗度越高权重越大），精度提升到亚像素
            double crossX =
                SubPixelCenter1D(colMeans, isRow: true, center: minColPt.X, halfWin: 30) + marginX;
            double crossY =
                SubPixelCenter1D(rowMeans, isRow: false, center: minRowPt.Y, halfWin: 30) + marginY;

            return new Point2d(crossX, crossY);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 在1D投影向量的指定邻域内，以亮度反转值为权重计算加权重心（亚像素精度）。
    /// </summary>
    /// <param name="vec">行向量（1×N）或列向量（N×1）</param>
    /// <param name="isRow">true=行向量（按 X 索引）；false=列向量（按 Y 索引）</param>
    /// <param name="center">邻域中心索引</param>
    /// <param name="halfWin">邻域半径（像素）</param>
    private static double SubPixelCenter1D(Mat vec, bool isRow, int center, int halfWin)
    {
        int len = isRow ? vec.Cols : vec.Rows;
        int lo = Math.Max(0, center - halfWin);
        int hi = Math.Min(len - 1, center + halfWin);

        double weightSum = 0;
        double posSum = 0;
        for (int i = lo; i <= hi; i++)
        {
            float val = isRow ? vec.At<float>(0, i) : vec.At<float>(i, 0);
            double weight = Math.Max(0.0, 255.0 - val); // 越暗权重越高
            weightSum += weight;
            posSum += weight * i;
        }

        return weightSum > 1e-6 ? posSum / weightSum : center;
    }

    /// <summary>
    /// 提取相机 X 方向焦距（像素单位）。
    /// 优先使用标定计算得到的内参矩阵 fx（精确值）；
    /// 若尚未完成内参标定，则用镜头标称焦距和像素尺寸估算：
    ///   fx = LensFocalLength(mm) × 1000 / PixelSizeUm(μm/pixel)
    /// </summary>
    private async Task<double?> GetFocalLengthPixelsAsync(Guid calibProjectId, Guid cameraDeviceId)
    {
        IQueryable<CalibCameraParam> q = await _cameraParamRepo.GetQueryableAsync();
        CalibCameraParam? param = await AsyncExecuter.FirstOrDefaultAsync(
            q.Where(x => x.CalibProjectId == calibProjectId && x.CameraDeviceId == cameraDeviceId)
        );
        if (param == null)
            return null;

        // 优先：标定内参 fx
        if (!string.IsNullOrEmpty(param.IntrinsicMatrixJson))
        {
            try
            {
                double[][]? matrix = JsonSerializer.Deserialize<double[][]>(
                    param.IntrinsicMatrixJson
                );
                double? fx = matrix?[0][0];
                if (fx is > 0)
                    return fx;
            }
            catch
            { /* 解析失败则回退到估算 */
            }
        }

        // 回退：用镜头焦距 + 像素物理尺寸估算
        // fx = LensFocalLength(mm) * 1000(μm/mm) / PixelSizeUm(μm/pixel)
        if (param.LensFocalLength > 0 && param.PixelSizeUm > 0)
            return (double)param.LensFocalLength * 1000.0 / (double)param.PixelSizeUm;

        return null;
    }

    /// <summary>将多圈累计位置归一化为单圈角度（0~35999 LSB）</summary>
    private static long NormalizeToSingleCircleLsb(long absolutePositionLsb)
    {
        long mod = absolutePositionLsb % 36000;
        return mod < 0 ? mod + 36000 : mod;
    }

    /// <summary>
    /// 轮询等待瓴控电机单圈角度到达目标位置（允许 0.1° = 10 LSB 圆周距离波动），超时返回 false。
    /// 使用圆周距离避免跨 0° 边界时误判（如目标 0 LSB，当前 35998 LSB，圆周距离仅 2 LSB）。
    /// </summary>
    private static async Task<bool> WaitUntilSingleAngleReachedAsync(
        KtechMotorDriver driver,
        long targetLsb,
        int timeoutSeconds,
        long toleranceLsb = 10
    )
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(200);
            uint current = await driver.ReadSingleAngleAsync();
            long dist = Math.Abs((long)current - targetLsb);
            // 圆周距离：跨 0°/360° 边界时取另一方向的距离
            dist = Math.Min(dist, 36000L - dist);
            if (dist <= toleranceLsb)
                return true;
        }
        return false;
    }

    /// <summary>轮询等待瓴控电机停止运动</summary>
    private static async Task WaitForKtechMotorStopAsync(
        KtechMotorDriver driver,
        int timeoutSeconds
    )
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(200);
            MotorStatus s = await driver.QueryStatusAsync();
            if (!s.IsMoving)
                return;
        }
    }
}
