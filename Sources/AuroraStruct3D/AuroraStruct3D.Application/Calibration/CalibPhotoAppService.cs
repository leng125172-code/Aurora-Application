using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AuroraStruct3D.Calibration.Dtos;
using AuroraStruct3D.Cameras;
using AuroraStruct3D.Cameras.Dtos;
using AuroraStruct3D.OpenCV.ImageOps;
using AuroraStruct3D.Projectors;
using AuroraStruct3D.Projectors.Dtos;
using AuroraStruct3D.Cameras.Tucam;
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
    private readonly IRepository<CalibProject, Guid> _projectRepo;
    private readonly IRepository<CalibCameraParam, Guid> _cameraParamRepo;
    private readonly IRepository<CalibPhotoRecord, Guid> _photoRepo;
    private readonly IRepository<CalibStereoResult, Guid> _stereoResultRepo;
    private readonly IBlobContainer<CalibPhotoBlobContainer> _blobContainer;
    private readonly IRepository<CameraDevice, Guid> _cameraDeviceRepository;
    private readonly ICameraDriverRegistry _cameraDrivers;
    private ITucamCameraService _tucamService =>
        _cameraDrivers.GetRequired("tucam") as ITucamCameraService
        ?? throw new UserFriendlyException("Tucam 驱动不可用");
    private readonly IProjectorDeviceAppService _projectorService;
    private readonly ILogger<CalibPhotoAppService> _logger;
    private readonly CalibBoardDetector _boardDetector;
    private readonly CalibExtrinsicSampler _extrinsicSampler;

    /// <summary>
    /// 进程级每相机触发模式锁：串行化 <see cref="GrabCalibFrameRawAsync"/> 的「读取→切换→恢复」序列。
    /// 应用服务为 Transient，故必须为 static 才能跨请求共享同一相机的锁。
    /// </summary>
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> _triggerModeLocks = new();

    /// <summary>构造注入</summary>
    public CalibPhotoAppService(
        IRepository<CalibProject, Guid> projectRepo,
        IRepository<CalibCameraParam, Guid> cameraParamRepo,
        IRepository<CalibPhotoRecord, Guid> photoRepo,
        IRepository<CalibStereoResult, Guid> stereoResultRepo,
        IBlobContainer<CalibPhotoBlobContainer> blobContainer,
        IRepository<CameraDevice, Guid> cameraDeviceRepository,
        ICameraDriverRegistry cameraDrivers,
        IProjectorDeviceAppService projectorService,
        ILogger<CalibPhotoAppService> logger,
        CalibBoardDetector boardDetector,
        CalibExtrinsicSampler extrinsicSampler
    )
    {
        _projectRepo = projectRepo;
        _cameraParamRepo = cameraParamRepo;
        _photoRepo = photoRepo;
        _stereoResultRepo = stereoResultRepo;
        _blobContainer = blobContainer;
        _cameraDeviceRepository = cameraDeviceRepository;
        _cameraDrivers = cameraDrivers;
        _projectorService = projectorService;
        _logger = logger;
        _boardDetector = boardDetector;
        _extrinsicSampler = extrinsicSampler;
    }

    /// <inheritdoc/>
    public async Task<CalibBoardConfigDto> UpdateBoardConfigAsync(UpdateBoardConfigInput input)
    {
        CalibProject project = await _projectRepo.GetAsync(input.CalibProjectId);
        _logger.LogInformation(
            "[标定配置] 更新标定板参数 — ProjectId={ProjectId}, BoardType={BoardType}, BoardThicknessMm={BoardThicknessMm}",
            input.CalibProjectId,
            input.BoardType,
            input.BoardThicknessMm
        );

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
            circle?.Detector is null ? null : JsonSerializer.Serialize(circle.Detector),
            input.BoardThicknessMm
        );

        _logger.LogInformation(
            "[标定配置] SetBoardConfig 后 — BoardThicknessMm={SavedBoardThicknessMm}",
            project.BoardThicknessMm
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
        Stopwatch totalSw = Stopwatch.StartNew();
        CalibProject project = await _projectRepo.GetAsync(input.CalibProjectId);
        CameraDevice camera = await _cameraDeviceRepository.GetAsync(input.CameraDeviceId);
        long metadataMs = totalSw.ElapsedMilliseconds;

        EnsureBoardConfigValid(project, isProjectedBoard: false);

        Stopwatch phaseSw = Stopwatch.StartNew();
        byte[] jpegBytes = await GrabCalibFrameRawAsync(
            input.CameraDeviceId,
            targetTriggerMode: 2,
            imageRotationAngle: camera.ImageRotationAngle
        );
        long captureMs = phaseSw.ElapsedMilliseconds;

        phaseSw.Restart();
        (bool isValid, int cornerCount) = _boardDetector.DetectBoardFeaturePoints(
            jpegBytes,
            project,
            isProjectedBoard: false
        );
        long detectionMs = phaseSw.ElapsedMilliseconds;

        string blobKey = BuildBlobKey(
            input.CalibProjectId,
            input.CameraDeviceId,
            CalibPhotoType.Intrinsic
        );
        phaseSw.Restart();
        await _blobContainer.SaveAsync(blobKey, jpegBytes, overrideExisting: false);
        long blobSaveMs = phaseSw.ElapsedMilliseconds;

        phaseSw.Restart();
        string imageBase64 = CreateThumbnailBase64(jpegBytes);

        CalibPhotoRecord record = new(
            GuidGenerator.Create(),
            input.CalibProjectId,
            input.CameraDeviceId,
            CalibPhotoType.Intrinsic,
            blobKey,
            isValid,
            cornerCount,
            imageBase64
        );
        await _photoRepo.InsertAsync(record);
        long recordSaveMs = phaseSw.ElapsedMilliseconds;

        _logger.LogInformation(
            "内参拍照耗时: Metadata={MetadataMs}ms, Capture={CaptureMs}ms, Detect={DetectionMs}ms, BlobSave={BlobSaveMs}ms, RecordAndBase64={RecordSaveMs}ms, Total={TotalMs}ms, ImageBytes={ImageBytes}, Valid={Valid}",
            metadataMs,
            captureMs,
            detectionMs,
            blobSaveMs,
            recordSaveMs,
            totalSw.ElapsedMilliseconds,
            jpegBytes.Length,
            isValid
        );

        return ToPhotoDto(record);
    }

    /// <inheritdoc/>
    public async Task<CalibPhotoDto> TakeExtrinsicDotPhotoAsync(TakeExtrinsicPhotoInput input)
    {
        CalibProject project = await _projectRepo.GetAsync(input.CalibProjectId);
        CameraDevice camera = await _cameraDeviceRepository.GetAsync(input.CameraDeviceId);

        EnsureBoardConfigValid(project, isProjectedBoard: false);

        Guid projectorDeviceId = ValidateAndGetProjectorId(project);
        Guid pairGroupId = GuidGenerator.Create();

        byte[] photoBytes;
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
            photoBytes = await GrabCalibFrameRawAsync(
                input.CameraDeviceId,
                targetTriggerMode: 2,
                imageRotationAngle: camera.ImageRotationAngle
            );
        }
        finally
        {
            await _projectorService.LedOffAsync(projectorDeviceId);
        }

        (bool isValid, int cornerCount) = _boardDetector.DetectBoardFeaturePoints(
            photoBytes,
            project,
            isProjectedBoard: false
        );

        string blobKey = BuildBlobKey(
            input.CalibProjectId,
            input.CameraDeviceId,
            CalibPhotoType.Extrinsic,
            ExtrinsicPhotoPhase.WhiteScreen,
            frameIndex: 0
        );
        await _blobContainer.SaveAsync(blobKey, photoBytes, overrideExisting: false);

        string imageBase64 = CreateThumbnailBase64(photoBytes);

        CalibPhotoRecord record = new(
            GuidGenerator.Create(),
            input.CalibProjectId,
            input.CameraDeviceId,
            CalibPhotoType.Extrinsic,
            blobKey,
            isValid,
            cornerCount,
            imageBase64,
            pairGroupId,
            stereoRole: null,
            extrinsicPhase: ExtrinsicPhotoPhase.WhiteScreen,
            imageDiffScore: null,
            imageDiffSignificant: null
        );
        await _photoRepo.InsertAsync(record);

        _logger.LogInformation(
            "[外参圆点拍照] 完成：PairGroupId={PairGroupId}, 角点数={CornerCount}, 有效={IsValid}",
            pairGroupId,
            cornerCount,
            isValid
        );

        return ToPhotoDto(record);
    }

    /// <inheritdoc/>
    public async Task<CalibPhotoDto> TakeExtrinsicCheckerboardPhotoAsync(
        TakeExtrinsicPhotoInput input
    )
    {
        CalibProject project = await _projectRepo.GetAsync(input.CalibProjectId);

        Guid projectorDeviceId = ValidateAndGetProjectorId(project);

        Guid? pairGroupId = null;
        try
        {
            IQueryable<CalibPhotoRecord> query = await _photoRepo.GetQueryableAsync();

            IQueryable<Guid?> alreadyPairedGroupIds = query
                .Where(q =>
                    q.CalibProjectId == input.CalibProjectId
                    && q.CameraDeviceId == input.CameraDeviceId
                    && q.ExtrinsicPhase == ExtrinsicPhotoPhase.Checkerboard
                )
                .Select(q => q.PairGroupId);

            CalibPhotoRecord? latestDotPhoto = await AsyncExecuter.FirstOrDefaultAsync(
                query
                    .Where(x =>
                        x.CalibProjectId == input.CalibProjectId
                        && x.CameraDeviceId == input.CameraDeviceId
                        && x.PhotoType == CalibPhotoType.Extrinsic
                        && x.ExtrinsicPhase == ExtrinsicPhotoPhase.WhiteScreen
                        && !alreadyPairedGroupIds.Contains(x.PairGroupId)
                    )
                    .OrderByDescending(x => x.CapturedAt)
            );

            if (latestDotPhoto != null)
            {
                pairGroupId = latestDotPhoto.PairGroupId;
            }
            else
            {
                pairGroupId = GuidGenerator.Create();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[外参拍照] 查询未配对白屏照片失败，将创建新配对组");
            pairGroupId = GuidGenerator.Create();
        }

        CameraDevice camera = await _cameraDeviceRepository.GetAsync(input.CameraDeviceId);

        byte[] photoBytes;
        try
        {
            await _projectorService.LedOnAsync(projectorDeviceId);

            if (project.ProjectedPixelSize > 0)
            {
                await _projectorService.SetCheckerboardPixelSizeAsync(
                    new SetProjectorCheckerboardDto
                    {
                        ProjectorDeviceId = projectorDeviceId,
                        PixelSize = project.ProjectedPixelSize,
                    }
                );
            }

            await _projectorService.SetDisplayModeAsync(
                new SetProjectorDisplayModeDto
                {
                    ProjectorDeviceId = projectorDeviceId,
                    Mode = ProjectorDisplayMode.Checkerboard,
                }
            );
            await Task.Delay(200);
            photoBytes = await GrabCalibFrameRawAsync(
                input.CameraDeviceId,
                targetTriggerMode: 2,
                imageRotationAngle: camera.ImageRotationAngle
            );
        }
        finally
        {
            await _projectorService.LedOffAsync(projectorDeviceId);
        }

        (bool isValid, int cornerCount) = _boardDetector.DetectBoardFeaturePoints(
            photoBytes,
            project,
            isProjectedBoard: true
        );

        string blobKey = BuildBlobKey(
            input.CalibProjectId,
            input.CameraDeviceId,
            CalibPhotoType.Extrinsic,
            ExtrinsicPhotoPhase.Checkerboard,
            frameIndex: 0
        );
        await _blobContainer.SaveAsync(blobKey, photoBytes, overrideExisting: false);

        string imageBase64 = CreateThumbnailBase64(photoBytes);

        CalibPhotoRecord record = new(
            GuidGenerator.Create(),
            input.CalibProjectId,
            input.CameraDeviceId,
            CalibPhotoType.Extrinsic,
            blobKey,
            isValid,
            cornerCount,
            imageBase64,
            pairGroupId ?? GuidGenerator.Create(),
            stereoRole: null,
            extrinsicPhase: ExtrinsicPhotoPhase.Checkerboard,
            imageDiffScore: null,
            imageDiffSignificant: null
        );
        await _photoRepo.InsertAsync(record);

        _logger.LogInformation(
            "[外参棋盘格拍照] 完成：PairGroupId={PairGroupId}, 角点数={CornerCount}, 有效={IsValid}",
            pairGroupId,
            cornerCount,
            isValid
        );

        return ToPhotoDto(record);
    }

    /// <inheritdoc/>
    public async Task<CalibStereoPairPhotoDto> TakeStereoExtrinsicPairPhotoAsync(
        TakeStereoExtrinsicPairPhotoInput input
    )
    {
        CalibProject project = await _projectRepo.GetAsync(input.CalibProjectId);

        EnsureBoardConfigValid(project, isProjectedBoard: false);

        if (!project.MainCameraDeviceId.HasValue || !project.SecondaryCameraDeviceId.HasValue)
        {
            throw new UserFriendlyException("当前项目未绑定主/从相机，无法进行双目联合外参拍照");
        }

        Guid mainCameraId = project.MainCameraDeviceId.Value;
        Guid secondaryCameraId = project.SecondaryCameraDeviceId.Value;

        CameraDevice mainCamera = await _cameraDeviceRepository.GetAsync(mainCameraId);
        CameraDevice secondaryCamera = await _cameraDeviceRepository.GetAsync(secondaryCameraId);
        _logger.LogInformation(
            "[双目标定] 相机配置 — 主相机旋转={MainRotate}°, 从相机旋转={SecRotate}°",
            mainCamera.ImageRotationAngle,
            secondaryCamera.ImageRotationAngle
        );

        // 主相机先拍，从相机再拍（_capStartActiveLock 保证顺序执行）
        // 两台均切换到软件触发模式进行标定拍照
        byte[] mainBytes = await GrabCalibFrameRawAsync(
            mainCameraId,
            targetTriggerMode: 2,
            imageRotationAngle: mainCamera.ImageRotationAngle
        );
        byte[] secondaryBytes = await GrabCalibFrameRawAsync(
            secondaryCameraId,
            targetTriggerMode: 2,
            imageRotationAngle: secondaryCamera.ImageRotationAngle
        );

        (bool mainValid, int mainCornerCount) = _boardDetector.DetectBoardFeaturePoints(
            mainBytes,
            project,
            isProjectedBoard: false
        );
        (bool secondaryValid, int secondaryCornerCount) = _boardDetector.DetectBoardFeaturePoints(
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

        string mainImageBase64 = CreateThumbnailBase64(mainBytes);
        string secondaryImageBase64 = CreateThumbnailBase64(secondaryBytes);

        CalibPhotoRecord mainRecord = new(
            GuidGenerator.Create(),
            input.CalibProjectId,
            mainCameraId,
            CalibPhotoType.StereoExtrinsicPair,
            mainBlobKey,
            mainValid,
            mainCornerCount,
            mainImageBase64,
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
            secondaryImageBase64,
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
        List<CalibPhotoRecord> records = await AsyncExecuter.ToListAsync(query);
        return records.Select(x => ToPhotoDto(x, compressLegacyThumbnail: true)).ToList();
    }

    /// <inheritdoc/>
    public async Task<IRemoteStreamContent> GetOriginalPhotoAsync(string blobKey)
    {
        if (string.IsNullOrWhiteSpace(blobKey))
            throw new UserFriendlyException("图片 Blob Key 不能为空");

        // Blob Key 必须属于现有标定照片，禁止利用该接口读取容器内任意文件。
        IQueryable<CalibPhotoRecord> query = await _photoRepo.GetQueryableAsync();
        bool exists = await AsyncExecuter.AnyAsync(query.Where(x => x.BlobKey == blobKey));
        if (!exists)
            throw new UserFriendlyException("标定原图不存在");

        byte[] bytes = await _blobContainer.GetAllBytesAsync(blobKey);
        bool isBmp =
            bytes.Length >= 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M';
        string contentType = isBmp ? "image/bmp" : "image/jpeg";
        string extension = isBmp ? "bmp" : "jpg";
        return new RemoteStreamContent(
            new MemoryStream(bytes, writable: false),
            $"calib-photo-original.{extension}",
            contentType
        );
    }

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

        HashSet<Guid> invalidExtrinsicGroupIds = CalibExtrinsicSampler
            .BuildExtrinsicSampleGroups(extrinsicPhotos)
            .Where(x => !x.IsValid)
            .Select(x => x.PairGroupId)
            .ToHashSet();

        HashSet<Guid> incompleteGroupIds = extrinsicPhotos
            .Where(x => x.PairGroupId.HasValue)
            .GroupBy(x => x.PairGroupId!.Value)
            .Where(g => g.Count() < 2)
            .Select(g => g.Key)
            .ToHashSet();

        invalidPhotos.AddRange(
            extrinsicPhotos.Where(x =>
                x.PairGroupId.HasValue
                    ? invalidExtrinsicGroupIds.Contains(x.PairGroupId.Value)
                        || incompleteGroupIds.Contains(x.PairGroupId.Value)
                    : !x.IsValid
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

        CameraDevice camera = await _cameraDeviceRepository.GetAsync(cameraDeviceId);
        _logger.LogInformation("[内参标定] 相机配置 — 旋转={Rotate}°", camera.ImageRotationAngle);

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

        (List<Mat> objectMats, List<Mat> imageMats, Size imageSize) =
            await CollectIntrinsicPointMatsAsync(
                intrinsicPhotos,
                worldCorners,
                patternSize,
                project,
                camera.ImageRotationAngle
            );

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
            // P0 优化：迭代剔除单帧误差 > max(2*mean, mean+2*stddev) 的离群帧后重新求解
            // 最多 3 轮，最小保留 10 帧；剔除帧的 PhotoId 和误差会写入日志
            reprojError = CalibrateCameraWithOutlierRejection(
                objectMats,
                imageMats,
                imageSize,
                cameraMatrix,
                distCoeffs,
                out rvecArray,
                out tvecArray,
                photos: intrinsicPhotos,
                minSamples: 10,
                maxIterations: 3,
                logPrefix: "[内参标定]"
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

        string intrinsicJson = CalibImageUtils.SerializeMatToJson(cameraMatrix);
        string distJson = CalibImageUtils.SerializeVecToJson(distCoeffs);

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

        _logger.LogInformation(
            "[内参标定] 完成，重投影误差 {Error:F4} px（阈值 {Threshold:F2} px），fx={Fx:F3}, fy={Fy:F3}, cx={Cx:F3}, cy={Cy:F3}, 畸变={Dist}",
            reprojError,
            maxReprojError,
            cameraMatrix.At<double>(0, 0),
            cameraMatrix.At<double>(1, 1),
            cameraMatrix.At<double>(0, 2),
            cameraMatrix.At<double>(1, 2),
            FormatDistCoeffsForLog(distCoeffs)
        );

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
        CameraDevice camera = await _cameraDeviceRepository.GetAsync(cameraDeviceId);
        _logger.LogInformation(
            "[外参标定] 从数据库读取项目 — BoardThicknessMm={BoardThicknessMm}, BoardType={BoardType}, Rotation={Rotate}°",
            project.BoardThicknessMm,
            project.BoardType,
            camera.ImageRotationAngle
        );

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

        if (!project.IsProjectorCalibrationRequired())
        {
            throw new UserFriendlyException(
                "双目结构光模式下不支持投影外参计算，结构光仅作为纹理生成器"
            );
        }

        // 从 DB 反序列化内参
        using Mat cameraMatrix = CalibImageUtils.DeserializeMatrix(camParam.IntrinsicMatrixJson);
        using Mat distCoeffs = CalibImageUtils.DeserializeVector(camParam.DistCoeffsJson);

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

        List<ProjectorExtrinsicSampleGroup> extrinsicPhotos = CalibExtrinsicSampler
            .BuildExtrinsicSampleGroups(extrinsicPhotoRecords)
            .Where(x => x.IsValid)
            .ToList();

        if (extrinsicPhotos.Count < CalibConsts.MinProjectorExtrinsicPhotoCount)
        {
            throw new UserFriendlyException(
                $"外参有效样本不足 {CalibConsts.MinProjectorExtrinsicPhotoCount} 组（当前 {extrinsicPhotos.Count} 组）"
            );
        }

        // SolvePnP：找最佳外参照片
        ProjectorExtrinsicSampleGroup? bestExtrinsic =
            await _extrinsicSampler.FindBestExtrinsicAsync(
                extrinsicPhotos,
                cameraMatrix,
                distCoeffs,
                patternSize,
                worldCorners,
                project,
                camera.ImageRotationAngle
            );

        string? rvecJson = null;
        string? tvecJson = null;
        double? projectorReprojectionError = null;

        if (bestExtrinsic != null)
        {
            Point2f[]? exCorners =
                await _extrinsicSampler.TryFindPhysicalBoardCornersFromSampleAsync(
                    bestExtrinsic,
                    patternSize,
                    project,
                    camera.ImageRotationAngle
                );
            if (exCorners != null)
            {
                int expectedCount = patternSize.Width * patternSize.Height;
                if (project.BoardType == CalibrationBoardType.MarkedSymmetricCircleGrid)
                    expectedCount--;
                _logger.LogInformation(
                    "[外参标定] 圆点板检测 — 角点数={CornerCount}, 期望={Expected}",
                    exCorners.Length,
                    expectedCount
                );

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
                _logger.LogInformation(
                    "[外参标定] SolvePnP 结果 — rvec=({Rx},{Ry},{Rz}), tvec=({Tx},{Ty},{Tz})",
                    rvec.At<double>(0, 0),
                    rvec.At<double>(1, 0),
                    rvec.At<double>(2, 0),
                    tvec.At<double>(0, 0),
                    tvec.At<double>(1, 0),
                    tvec.At<double>(2, 0)
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
                double maxErr = 0;
                double minErr = double.MaxValue;
                for (int i = 0; i < exCorners.Length; i++)
                {
                    double dx = exCorners[i].X - projected[i].X;
                    double dy = exCorners[i].Y - projected[i].Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    err += dist;
                    maxErr = Math.Max(maxErr, dist);
                    minErr = Math.Min(minErr, dist);
                }
                projectorReprojectionError = err / exCorners.Length;
                _logger.LogInformation(
                    "[外参标定] 重投影误差分布 — 平均={Avg:F4}px, 最大={Max:F4}px, 最小={Min:F4}px",
                    projectorReprojectionError.Value,
                    maxErr,
                    minErr
                );
                rvecJson = CalibImageUtils.SerializeVecToJson(rvec);
                tvecJson = CalibImageUtils.SerializeVecToJson(tvec);
            }
        }

        double maxProjReprojError =
            project.BoardType == CalibrationBoardType.Chessboard
                ? CalibConsts.MaxSingleCameraReprojectionError
                : CalibConsts.MaxCircleBoardReprojectionError;

        if (projectorReprojectionError.HasValue)
        {
            _logger.LogInformation(
                "[外参标定] 投影仪外参重投影误差 — 实际={Actual:F4}px, 阈值={Threshold:F2}px",
                projectorReprojectionError.Value,
                maxProjReprojError
            );
        }

        if (
            projectorReprojectionError.HasValue
            && projectorReprojectionError.Value > maxProjReprojError
        )
        {
            throw new UserFriendlyException(
                $"外参重投影误差 {projectorReprojectionError.Value:F4} px，超过阈值 {maxProjReprojError:F2} px，请补拍后重算"
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
                Point3f[]? objPts = await _extrinsicSampler.BuildProjectorObjectPointsCamAsync(
                    sample,
                    cameraMatrix,
                    distCoeffs,
                    project,
                    patternSize,
                    worldCorners,
                    projPatternSize,
                    projPixels.Length,
                    camera.ImageRotationAngle
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
            using Mat projKMat = new(3, 3, MatType.CV_64FC1);
            projKMat.SetTo(Scalar.All(0));
            projKMat.Set(0, 0, projectorSize.Width / 2.0);
            projKMat.Set(1, 1, projectorSize.Height / 2.0);
            projKMat.Set(0, 2, projectorSize.Width / 2.0);
            projKMat.Set(1, 2, projectorSize.Height / 2.0);
            projKMat.Set(2, 2, 1.0);

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
                    CalibrationFlags.UseIntrinsicGuess
                );
                projIntrinsicJson = CalibImageUtils.SerializeMatToJson(projKMat);
                projDistJson = CalibImageUtils.SerializeVecToJson(projDistMat);

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
                camToProjectorRJson = CalibImageUtils.SerializeMatToJson(R_cp_ortho);
                camToProjectorTJson = CalibImageUtils.SerializeVecToJson(t_cp_avg);
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
            tvecJson
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

        CameraDevice camera = await _cameraDeviceRepository.GetAsync(cameraDeviceId);
        _logger.LogInformation("[单目标定] 相机配置 — 旋转={Rotate}°", camera.ImageRotationAngle);

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

            extrinsicPhotos = CalibExtrinsicSampler
                .BuildExtrinsicSampleGroups(extrinsicPhotoRecords)
                .Where(x => x.IsValid)
                .ToList();
        }

        // ── 计算内参 ─────────────────────────────────────────────────────────────
        Size patternSize = GetBoardPatternSize(project, isProjectedBoard: false);
        float spacingMm = GetBoardSpacingMm(project);

        // 构建世界坐标（标定板平面，Z=0）
        Point3f[] worldCorners = BuildBoardWorldPoints(project, patternSize, spacingMm);

        System.Diagnostics.Stopwatch phaseSw = new();
        (List<Mat> objectMats, List<Mat> imageMats, Size imageSize) =
            await CollectIntrinsicPointMatsAsync(
                intrinsicPhotos,
                worldCorners,
                patternSize,
                project,
                camera.ImageRotationAngle,
                "单目标定"
            );

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
        Mat[] rvecArray = [];
        Mat[] tvecArray = [];
        try
        {
            // P0 优化：迭代剔除单帧误差 > max(2*mean, mean+2*stddev) 的离群帧后重新求解
            // 最多 3 轮，最小保留 10 帧；剔除帧的 PhotoId 和误差会写入日志
            // 注：若 CollectIntrinsicPointMatsAsync 跳过了失败帧导致 objectMats.Count < intrinsicPhotos.Count，
            //     方法内部会自动降级为仅记录原索引（不记录 PhotoId/FileName），避免索引错位
            reprojError = CalibrateCameraWithOutlierRejection(
                objectMats,
                imageMats,
                imageSize,
                cameraMatrix,
                distCoeffs,
                out rvecArray,
                out tvecArray,
                photos: intrinsicPhotos,
                minSamples: 10,
                maxIterations: 3,
                logPrefix: "[单目标定]"
            );
        }
        finally
        {
            foreach (Mat m in objectMats)
                m.Dispose();
            foreach (Mat m in imageMats)
                m.Dispose();
            foreach (Mat m in rvecArray)
                m.Dispose();
            foreach (Mat m in tvecArray)
                m.Dispose();
        }

        _logger.LogInformation(
            "[单目标定] CalibrateCamera 耗时 {Ms}ms，重投影误差 {Error:F4} px",
            phaseSw.ElapsedMilliseconds,
            reprojError
        );

        // 序列化内参
        string intrinsicJson = CalibImageUtils.SerializeMatToJson(cameraMatrix);
        string distJson = CalibImageUtils.SerializeVecToJson(distCoeffs);

        // ── 计算外参（使用第一张有效外参照片）─────────────────────────────────────
        string? rvecJson = null;
        string? tvecJson = null;
        double? projectorReprojectionError = null;

        bool shouldComputeProjectorExtrinsic =
            project.DeviceSeries == DeviceSeries.SingleLight
            && project.IsProjectorCalibrationRequired();

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
            ProjectorExtrinsicSampleGroup? bestExtrinsic =
                await _extrinsicSampler.FindBestExtrinsicAsync(
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
                Point2f[]? exCorners =
                    await _extrinsicSampler.TryFindPhysicalBoardCornersFromSampleAsync(
                        bestExtrinsic,
                        patternSize,
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

                    rvecJson = CalibImageUtils.SerializeVecToJson(rvec);
                    tvecJson = CalibImageUtils.SerializeVecToJson(tvec);
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
                    Point3f[]? objPts = await _extrinsicSampler.BuildProjectorObjectPointsCamAsync(
                        sample,
                        cameraMatrix,
                        distCoeffs,
                        project,
                        patternSize,
                        worldCorners,
                        projPatternSize,
                        projPixels.Length,
                        camera.ImageRotationAngle
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
                        "[单目标定] 投影仪内参计算完成，耗时 {Ms}ms，重投影误差 {Error:F4} px",
                        phaseSw.ElapsedMilliseconds,
                        projCalibReprojError
                    );

                    projIntrinsicJson = CalibImageUtils.SerializeMatToJson(projKMat);
                    projDistJson = CalibImageUtils.SerializeVecToJson(projDistMat);

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

                    camToProjectorRJson = CalibImageUtils.SerializeMatToJson(R_cp_ortho);
                    camToProjectorTJson = CalibImageUtils.SerializeVecToJson(t_cp_avg);

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

        if (projectorReprojectionError.HasValue)
        {
            _logger.LogInformation(
                "[外参标定] 投影仪外参重投影误差 — 实际={Actual:F4}px, 阈值={Threshold:F2}px",
                projectorReprojectionError.Value,
                maxSingleReprojError
            );
        }

        if (
            shouldComputeProjectorExtrinsic
            && projectorReprojectionError.HasValue
            && projectorReprojectionError.Value > maxSingleReprojError
        )
        {
            throw new UserFriendlyException(
                $"投影外参重投影误差为 {projectorReprojectionError.Value:F4} px，超过阈值 {maxSingleReprojError:F2} px，请补拍后重算"
            );
        }

        // ── 持久化结果到 CalibCameraParam ────────────────────────────────────────
        IQueryable<CalibCameraParam> paramQuery = await _cameraParamRepo.GetQueryableAsync();
        CalibCameraParam? camParam = await AsyncExecuter.FirstOrDefaultAsync(
            paramQuery.Where(x =>
                x.CalibProjectId == calibProjectId && x.CameraDeviceId == cameraDeviceId
            )
        );

        if (camParam != null)
        {
            camParam.SetCalibResult(intrinsicJson, distJson, reprojError, rvecJson, tvecJson);
            await _cameraParamRepo.UpdateAsync(camParam);
        }

        return new CalibComputeResultDto
        {
            IntrinsicMatrixJson = intrinsicJson,
            DistCoeffsJson = distJson,
            ReprojectionError = reprojError,
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
        List<ProjectorExtrinsicSampleGroup> extrinsicGroups =
            CalibExtrinsicSampler.BuildExtrinsicSampleGroups(
                all.Where(x => x.PhotoType == CalibPhotoType.Extrinsic).ToList()
            );
        int extrinsicTotal = extrinsicGroups.Count;
        int extrinsicValid = extrinsicGroups.Count(x => x.IsValid);

        int stereoTotal = all.Count(x => x.PhotoType == CalibPhotoType.StereoExtrinsicPair);
        int stereoValid = all.Count(x =>
            x.PhotoType == CalibPhotoType.StereoExtrinsicPair && x.IsValid
        );

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
            StereoTotal = stereoTotal,
            StereoValid = stereoValid,
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

        CameraDevice mainCamera = await _cameraDeviceRepository.GetAsync(mainCameraId);
        CameraDevice secondaryCamera = await _cameraDeviceRepository.GetAsync(secondaryCameraId);
        _logger.LogInformation(
            "[双目标定] 相机配置 — 主相机旋转={MainRotate}°, 从相机旋转={SecRotate}°",
            mainCamera.ImageRotationAngle,
            secondaryCamera.ImageRotationAngle
        );

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

        _logger.LogInformation(
            "[双目标定] 分组结果 — 总组数={TotalGroups}, 有效组数={ValidGroups}, 主相机ID={MainId}, 从相机ID={SecId}",
            pairPhotos.GroupBy(x => x.PairGroupId!.Value).Count(),
            validPairs.Count,
            mainCameraId,
            secondaryCameraId
        );

        foreach (var (main, secondary) in validPairs)
        {
            _logger.LogInformation(
                "[双目标定] 配对 [{GroupId}] — 主照片ID={MainPhotoId}, 从照片ID={SecPhotoId}, 主角点数={MainCorners}, 从角点数={SecCorners}",
                main.PairGroupId,
                main.Id,
                secondary.Id,
                main.CornerCountDetected,
                secondary.CornerCountDetected
            );
        }

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
        _logger.LogInformation(
            "[双目标定] 标定板配置 — BoardType={BoardType}, PatternSize={W}x{H}, Spacing={Spacing}mm, WorldPoints={Count}",
            project.BoardType,
            patternSize.Width,
            patternSize.Height,
            spacingMm,
            worldCorners.Length
        );
        if (worldCorners.Length > 0)
        {
            _logger.LogInformation(
                "[双目标定] 世界点坐标示例 — 原点({X0:F2}, {Y0:F2}, {Z0:F2}), 中点({X1:F2}, {Y1:F2}, {Z1:F2}), 末点({X2:F2}, {Y2:F2}, {Z2:F2})",
                worldCorners[0].X,
                worldCorners[0].Y,
                worldCorners[0].Z,
                worldCorners[worldCorners.Length / 2].X,
                worldCorners[worldCorners.Length / 2].Y,
                worldCorners[worldCorners.Length / 2].Z,
                worldCorners[^1].X,
                worldCorners[^1].Y,
                worldCorners[^1].Z
            );
        }

        List<Mat> objectPoints = [];
        List<Mat> imagePointsMain = [];
        List<Mat> imagePointsSecondary = [];
        List<Point2f[]> detectedPointsMain = [];
        List<Point2f[]> detectedPointsSecondary = [];
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
            // 注意：拍照阶段 GrabFrameRawAsync → EncodeToBmp 已通过 RotateInterleavedPixels
            //       将旋转烘焙进 BMP 像素，Blob 中保存的 BMP 已是旋转后的图像。
            //       计算阶段若再次调用 LoadGrayMatWithRotation 会产生"二次旋转"，导致左右相机
            //       坐标系错乱，双目重投误差可达 100px+。故此处使用不旋转的 LoadGrayMat。
            phaseSwS.Restart();
            using Mat mainMat = CalibImageUtils.LoadGrayMat(mainBytes);
            using Mat secondaryMat = CalibImageUtils.LoadGrayMat(secondaryBytes);
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
            Point2f[]? mainCorners = _boardDetector.FindBoardPointsSubpixGray(
                mainMat,
                patternSize,
                project.BoardType,
                project
            );
            Point2f[]? secondaryCorners = _boardDetector.FindBoardPointsSubpixGray(
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

            if (_si == 0)
            {
                _logger.LogInformation(
                    "[双目标定] 第1帧角点坐标对比 — 主相机原点({MX0:F2},{MY0:F2}), 末点({MX1:F2},{MY1:F2}); 从相机原点({SX0:F2},{SY0:F2}), 末点({SX1:F2},{SY1:F2})",
                    mainCorners[0].X,
                    mainCorners[0].Y,
                    mainCorners[^1].X,
                    mainCorners[^1].Y,
                    secondaryCorners[0].X,
                    secondaryCorners[0].Y,
                    secondaryCorners[^1].X,
                    secondaryCorners[^1].Y
                );
            }

            objectPoints.Add(Mat.FromArray(worldCorners));
            imagePointsMain.Add(Mat.FromArray(mainCorners));
            imagePointsSecondary.Add(Mat.FromArray(secondaryCorners));
            detectedPointsMain.Add(mainCorners);
            detectedPointsSecondary.Add(secondaryCorners);
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

        using Mat mainCameraMatrix = CalibImageUtils.DeserializeMatrix(
            mainParam.IntrinsicMatrixJson!
        );
        using Mat mainDistCoeffs = CalibImageUtils.DeserializeVector(mainParam.DistCoeffsJson!);
        using Mat secondaryCameraMatrix = CalibImageUtils.DeserializeMatrix(
            secondaryParam.IntrinsicMatrixJson!
        );
        using Mat secondaryDistCoeffs = CalibImageUtils.DeserializeVector(
            secondaryParam.DistCoeffsJson!
        );

        _logger.LogInformation(
            "[双目标定] 主相机内参 — fx={Fx:F2}, fy={Fy:F2}, cx={Cx:F2}, cy={Cy:F2}",
            mainCameraMatrix.At<double>(0, 0),
            mainCameraMatrix.At<double>(1, 1),
            mainCameraMatrix.At<double>(0, 2),
            mainCameraMatrix.At<double>(1, 2)
        );
        _logger.LogInformation(
            "[双目标定] 从相机内参 — fx={Fx:F2}, fy={Fy:F2}, cx={Cx:F2}, cy={Cy:F2}",
            secondaryCameraMatrix.At<double>(0, 0),
            secondaryCameraMatrix.At<double>(1, 1),
            secondaryCameraMatrix.At<double>(0, 2),
            secondaryCameraMatrix.At<double>(1, 2)
        );

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

        if (stereoError > 10)
        {
            _logger.LogError(
                "[双目标定] 重投影误差异常({Error:F4}px)，输出结果矩阵 — R=[{R00:F4},{R01:F4},{R02:F4};{R10:F4},{R11:F4},{R12:F4};{R20:F4},{R21:F4},{R22:F4}], T=[{T0:F4},{T1:F4},{T2:F4}]",
                stereoError,
                r.At<double>(0, 0),
                r.At<double>(0, 1),
                r.At<double>(0, 2),
                r.At<double>(1, 0),
                r.At<double>(1, 1),
                r.At<double>(1, 2),
                r.At<double>(2, 0),
                r.At<double>(2, 1),
                r.At<double>(2, 2),
                t.At<double>(0),
                t.At<double>(1),
                t.At<double>(2)
            );
            _logger.LogError(
                "[双目标定] 异常诊断 — 样本数={Count}, 图像尺寸={W}x{H}, 主相机内参有效性={MainValid}, 从相机内参有效性={SecValid}",
                objectPoints.Count,
                imageSize.Width,
                imageSize.Height,
                mainParam.ReprojectionError.HasValue && mainParam.ReprojectionError.Value < 1,
                secondaryParam.ReprojectionError.HasValue
                    && secondaryParam.ReprojectionError.Value < 1
            );
        }

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

        double translationNorm = Math.Sqrt(
            Math.Pow(t.At<double>(0), 2)
                + Math.Pow(t.At<double>(1), 2)
                + Math.Pow(t.At<double>(2), 2)
        );
        using Mat projectionBaseline =
            StereoReconstructionUtils.ComputeBaselineFromStereoResult(p1, p2);
        double projectionBaselineNorm =
            StereoReconstructionUtils.ComputeBaselineDistance(projectionBaseline);
        double rectifiedCx1 = p1.At<double>(0, 2);
        double rectifiedCx2 = p2.At<double>(0, 2);
        int disparitySign = StereoReconstructionUtils.ComputeDisparitySign(p1, p2);

        _logger.LogInformation(
            "[双目标定] 联合外参 — R={R}, T={T}, RotationAngle={RotationAngle:F3}deg, TranslationNorm={TranslationNorm:F3}mm",
            CalibImageUtils.SerializeMatToJson(r),
            CalibImageUtils.SerializeVecToJson(t),
            Math.Acos(Math.Clamp((Cv2.Trace(r).Val0 - 1d) / 2d, -1d, 1d))
                * 180d
                / Math.PI,
            translationNorm
        );
        _logger.LogInformation(
            "[双目标定] 整平投影矩阵 — P1={P1}, P2={P2}, Q={Q}",
            CalibImageUtils.SerializeMatToJson(p1),
            CalibImageUtils.SerializeMatToJson(p2),
            CalibImageUtils.SerializeMatToJson(q)
        );
        _logger.LogInformation(
            "[双目标定] 几何一致性 — TranslationBaseline={TranslationBaseline:F3}mm, ProjectionBaseline={ProjectionBaseline:F3}mm, Difference={Difference:F3}mm, "
                + "RectifiedFx={Fx:F3}, Cx1={Cx1:F3}, Cx2={Cx2:F3}, CxDifference={CxDifference:F3}, DisparityDirection={Direction}",
            translationNorm,
            projectionBaselineNorm,
            Math.Abs(translationNorm - projectionBaselineNorm),
            p1.At<double>(0, 0),
            rectifiedCx1,
            rectifiedCx2,
            rectifiedCx1 - rectifiedCx2,
            disparitySign > 0 ? "Positive" : "Negative"
        );

        List<double> verticalErrors = [];
        List<double> horizontalDisparities = [];
        for (int pairIndex = 0; pairIndex < detectedPointsMain.Count; pairIndex++)
        {
            using Mat rectifiedMainPoints = new();
            using Mat rectifiedSecondaryPoints = new();
            Cv2.UndistortPoints(
                InputArray.Create(detectedPointsMain[pairIndex]),
                rectifiedMainPoints,
                mainCameraMatrix,
                mainDistCoeffs,
                r1,
                p1
            );
            Cv2.UndistortPoints(
                InputArray.Create(detectedPointsSecondary[pairIndex]),
                rectifiedSecondaryPoints,
                secondaryCameraMatrix,
                secondaryDistCoeffs,
                r2,
                p2
            );
            rectifiedMainPoints.GetArray(out Point2f[] mainRectified);
            rectifiedSecondaryPoints.GetArray(out Point2f[] secondaryRectified);
            int pointCount = Math.Min(mainRectified.Length, secondaryRectified.Length);
            for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                verticalErrors.Add(
                    Math.Abs(mainRectified[pointIndex].Y - secondaryRectified[pointIndex].Y)
                );
                horizontalDisparities.Add(
                    mainRectified[pointIndex].X - secondaryRectified[pointIndex].X
                );
            }
        }

        if (verticalErrors.Count > 0)
        {
            verticalErrors.Sort();
            horizontalDisparities.Sort();
            int p95Index = Math.Min(
                verticalErrors.Count - 1,
                (int)Math.Ceiling(verticalErrors.Count * 0.95d) - 1
            );
            _logger.LogInformation(
                "[双目标定] 整平极线验收 — Points={Points}, VerticalErrorMean={Mean:F4}px, P95={P95:F4}px, Max={Max:F4}px, "
                    + "HorizontalDisparityRange=[{MinDisparity:F3},{MaxDisparity:F3}]",
                verticalErrors.Count,
                verticalErrors.Average(),
                verticalErrors[p95Index],
                verticalErrors[^1],
                horizontalDisparities[0],
                horizontalDisparities[^1]
            );
        }

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

        // 重新计算双目时旧的 map 文件已存在，必须允许覆盖（map 为可重建的派生数据）
        await _blobContainer.SaveAsync(map1XBlobKey, SerializeFloatMapToBinary(map1x), true);
        await _blobContainer.SaveAsync(map1YBlobKey, SerializeFloatMapToBinary(map1y), true);
        await _blobContainer.SaveAsync(map2XBlobKey, SerializeFloatMapToBinary(map2x), true);
        await _blobContainer.SaveAsync(map2YBlobKey, SerializeFloatMapToBinary(map2y), true);
        _logger.LogInformation(
            "[双目标定] 4张 map 序列化并写入 BLOB 耗时 {Ms}ms",
            phaseSwS.ElapsedMilliseconds
        );

        string rJson = CalibImageUtils.SerializeMatToJson(r);
        string tJson = CalibImageUtils.SerializeVecToJson(t);
        string lToRJson = CalibImageUtils.SerializeTransformToJson(r, t);
        string rToLJson = CalibImageUtils.SerializeInverseTransformToJson(r, t);
        string r1Json = CalibImageUtils.SerializeMatToJson(r1);
        string r2Json = CalibImageUtils.SerializeMatToJson(r2);
        string p1Json = CalibImageUtils.SerializeMatToJson(p1);
        string p2Json = CalibImageUtils.SerializeMatToJson(p2);

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
    /// 内参标定：迭代剔除高重投影误差帧后重新求解（P0 优化）。
    /// 每轮调用 <see cref="Cv2.CalibrateCamera"/> 后用 <see cref="Cv2.ProjectPoints"/> 计算每帧 RMS 误差，
    /// 剔除误差 &gt; max(2*mean, mean+2*stddev) 的帧，重新求解，最多迭代 3 轮。
    /// 不释放输入的 objectMats/imageMats（由调用方在 finally 中释放）；
    /// 每轮产生的 rvec/tvec 在迭代中释放，仅最终轮返回给调用方。
    /// </summary>
    /// <param name="objectMats">世界坐标点 Mat 列表（每帧一个 Mat，CV_32FC3）</param>
    /// <param name="imageMats">图像坐标点 Mat 列表（每帧一个 Mat，CV_32FC2）</param>
    /// <param name="imageSize">图像分辨率</param>
    /// <param name="cameraMatrix">输出相机内参矩阵</param>
    /// <param name="distCoeffs">输出畸变系数</param>
    /// <param name="rvecArray">输出每帧旋转向量</param>
    /// <param name="tvecArray">输出每帧平移向量</param>
    /// <param name="photos">对应照片记录列表，用于日志记录被剔除帧的 ID 和文件名（可选）</param>
    /// <param name="minSamples">最小保留帧数，剔除后低于此值则停止（默认 10）</param>
    /// <param name="maxIterations">最大迭代轮数（默认 3）</param>
    /// <param name="logPrefix">日志前缀（如"[内参标定]"）</param>
    /// <returns>最终整体 RMS 重投影误差</returns>
    private double CalibrateCameraWithOutlierRejection(
        List<Mat> objectMats,
        List<Mat> imageMats,
        Size imageSize,
        Mat cameraMatrix,
        Mat distCoeffs,
        out Mat[] rvecArray,
        out Mat[] tvecArray,
        List<CalibPhotoRecord>? photos = null,
        int minSamples = 10,
        int maxIterations = 3,
        string logPrefix = "[内参标定]"
    )
    {
        if (objectMats.Count != imageMats.Count)
            throw new ArgumentException("objectMats 和 imageMats 数量不一致");
        if (objectMats.Count == 0)
            throw new ArgumentException("objectMats 不能为空");

        // 当前保留的索引（指向原始 objectMats/imageMats）
        List<int> keptIndices = Enumerable.Range(0, objectMats.Count).ToList();
        double lastError = 0;
        rvecArray = Array.Empty<Mat>();
        tvecArray = Array.Empty<Mat>();

        for (int iter = 0; iter < maxIterations; iter++)
        {
            // 构建子集（直接引用原 Mat，不 Clone，避免内存翻倍）
            List<Mat> subsetObjects = new(keptIndices.Count);
            List<Mat> subsetImages = new(keptIndices.Count);
            foreach (int idx in keptIndices)
            {
                subsetObjects.Add(objectMats[idx]);
                subsetImages.Add(imageMats[idx]);
            }

            // 释放上一轮的 rvec/tvec（最终轮返回给调用方）
            if (rvecArray.Length > 0)
            {
                foreach (Mat m in rvecArray)
                    m.Dispose();
                foreach (Mat m in tvecArray)
                    m.Dispose();
                rvecArray = Array.Empty<Mat>();
                tvecArray = Array.Empty<Mat>();
            }

            try
            {
                lastError = Cv2.CalibrateCamera(
                    subsetObjects,
                    subsetImages,
                    imageSize,
                    cameraMatrix,
                    distCoeffs,
                    out rvecArray,
                    out tvecArray,
                    CalibrationFlags.None
                );
            }
            catch
            {
                if (iter > 0)
                {
                    _logger.LogWarning(
                        "{Prefix} 第 {Iter} 轮 CalibrateCamera 失败，回退到上一轮结果",
                        logPrefix,
                        iter + 1
                    );
                    return lastError;
                }
                throw;
            }

            // 用 ProjectPoints 重投影每帧角点，计算每帧 RMS 误差
            double[] perViewErrors = ComputePerViewReprojectionErrors(
                subsetObjects,
                subsetImages,
                cameraMatrix,
                distCoeffs,
                rvecArray,
                tvecArray
            );

            // 输出内参矩阵与畸变系数，方便排查不同轮次之间的变化
            double fx = cameraMatrix.At<double>(0, 0);
            double fy = cameraMatrix.At<double>(1, 1);
            double cx = cameraMatrix.At<double>(0, 2);
            double cy = cameraMatrix.At<double>(1, 2);
            string distStr = FormatDistCoeffsForLog(distCoeffs);

            _logger.LogInformation(
                "{Prefix} 第 {Iter} 轮标定完成 — 保留 {Count} 帧，整体 RMS={Error:F4} px，"
                    + "单帧误差 min={Min:F4} max={Max:F4} mean={Mean:F4}; 内参 fx={Fx:F3}, fy={Fy:F3}, cx={Cx:F3}, cy={Cy:F3}; 畸变={Dist}",
                logPrefix,
                iter + 1,
                keptIndices.Count,
                lastError,
                perViewErrors.Min(),
                perViewErrors.Max(),
                perViewErrors.Average(),
                fx,
                fy,
                cx,
                cy,
                distStr
            );

            // 输出每帧误差明细（按原始索引顺序），便于定位具体哪几张图差
            LogPerViewErrors(logPrefix, iter + 1, keptIndices, perViewErrors, photos);

            // 最后一轮或样本数已不足，停止迭代
            if (iter == maxIterations - 1 || keptIndices.Count <= minSamples)
                break;

            // 计算均值与标准差，确定剔除阈值
            // 阈值取 max(2*mean, mean+2*stddev)，可同时应对：
            //   1) 所有帧误差相近时（stddev 小），用 2*mean 避免误剔
            //   2) 存在极端离群帧时（mean 被拉高），用 mean+2*stddev 才能剔掉最差的
            double mean = perViewErrors.Average();
            double stddev = Math.Sqrt(perViewErrors.Select(e => (e - mean) * (e - mean)).Average());
            double threshold = Math.Max(2.0 * mean, mean + 2.0 * stddev);

            _logger.LogInformation(
                "{Prefix} 第 {Iter} 轮阈值明细 — mean={Mean:F4}, stddev={Stddev:F4}, 2*mean={TwoMean:F4}, mean+2*stddev={MeanTwoStd:F4}, 最终阈值={Threshold:F4}",
                logPrefix,
                iter + 1,
                mean,
                stddev,
                2.0 * mean,
                mean + 2.0 * stddev,
                threshold
            );

            // 找出超阈值的索引
            List<(int KeptIdx, int OrigIdx, double Err)> toReject = new();
            for (int i = 0; i < perViewErrors.Length; i++)
            {
                if (perViewErrors[i] > threshold)
                    toReject.Add((i, keptIndices[i], perViewErrors[i]));
            }

            if (toReject.Count == 0)
            {
                _logger.LogInformation(
                    "{Prefix} 第 {Iter} 轮未发现离群帧，迭代结束",
                    logPrefix,
                    iter + 1
                );
                break;
            }

            // 检查剔除后是否仍满足最小样本数
            if (keptIndices.Count - toReject.Count < minSamples)
            {
                _logger.LogWarning(
                    "{Prefix} 第 {Iter} 轮发现 {RejectCount} 个离群帧，但剔除后剩余 {RemainCount} < {MinSamples}，停止剔除",
                    logPrefix,
                    iter + 1,
                    toReject.Count,
                    keptIndices.Count - toReject.Count,
                    minSamples
                );
                break;
            }

            // 记录被剔除帧的日志（便于后续分析是否为相机硬件问题）
            // 仅当 photos 与 objectMats 数量一致时才记录 PhotoId/BlobKey，避免索引错位
            bool canMapPhotos = photos != null && photos.Count == objectMats.Count;
            foreach (var r in toReject)
            {
                Guid? photoId = canMapPhotos ? photos![r.OrigIdx].Id : null;
                string? blobKey = canMapPhotos ? photos![r.OrigIdx].BlobKey : null;
                _logger.LogWarning(
                    "{Prefix} 第 {Iter} 轮剔除离群帧 — 原索引={OrigIdx}, PhotoId={PhotoId}, BlobKey={BlobKey}, 误差={Err:F4} px, 阈值={Thr:F4} px",
                    logPrefix,
                    iter + 1,
                    r.OrigIdx,
                    photoId,
                    blobKey,
                    r.Err,
                    threshold
                );
            }

            // 从 keptIndices 中移除被剔除的（按 KeptIdx 倒序删除，避免索引错位）
            foreach (var r in toReject.OrderByDescending(x => x.KeptIdx))
            {
                keptIndices.RemoveAt(r.KeptIdx);
            }
        }

        return lastError;
    }

    /// <summary>
    /// 用 <see cref="Cv2.ProjectPoints"/> 重投影每帧角点，计算每帧 RMS 重投影误差。
    /// 用于迭代剔除离群帧的判定依据。
    /// </summary>
    private static double[] ComputePerViewReprojectionErrors(
        List<Mat> objectMats,
        List<Mat> imageMats,
        Mat cameraMatrix,
        Mat distCoeffs,
        Mat[] rvecArray,
        Mat[] tvecArray
    )
    {
        double[] errors = new double[objectMats.Count];
        for (int i = 0; i < objectMats.Count; i++)
        {
            using Mat projected = new();
            Cv2.ProjectPoints(
                objectMats[i],
                rvecArray[i],
                tvecArray[i],
                cameraMatrix,
                distCoeffs,
                projected
            );

            // projected 与 imageMats[i] 均为 N×1 的 CV_32FC2 矩阵，逐元素访问计算误差
            int n = (int)imageMats[i].Total();
            double errSum = 0;
            for (int j = 0; j < n; j++)
            {
                Vec2f proj = projected.At<Vec2f>(0, j);
                Vec2f obs = imageMats[i].At<Vec2f>(0, j);
                double dx = proj.Item0 - obs.Item0;
                double dy = proj.Item1 - obs.Item1;
                errSum += (dx * dx) + (dy * dy);
            }
            errors[i] = Math.Sqrt(errSum / n);
        }
        return errors;
    }

    /// <summary>
    /// 将畸变系数 Mat 格式化为日志字符串（最多 8 个系数）。
    /// </summary>
    private static string FormatDistCoeffsForLog(Mat distCoeffs)
    {
        if (distCoeffs.Empty() || distCoeffs.Total() == 0)
            return "[]";

        int count = (int)distCoeffs.Total();
        StringBuilder sb = new StringBuilder("[");
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append(
                distCoeffs
                    .At<double>(0, i)
                    .ToString("F6", System.Globalization.CultureInfo.InvariantCulture)
            );
        }
        sb.Append(']');
        return sb.ToString();
    }

    /// <summary>
    /// 记录每帧重投影误差明细，并按误差从大到小输出 top N，便于排查具体哪几张图较差。
    /// </summary>
    private void LogPerViewErrors(
        string logPrefix,
        int iteration,
        List<int> keptIndices,
        double[] perViewErrors,
        List<CalibPhotoRecord>? photos
    )
    {
        bool canMapPhotos = photos != null && photos.Count >= keptIndices.Max() + 1;

        // 全部明细（按原始索引升序）
        StringBuilder detailSb = new StringBuilder();
        detailSb.Append($"{logPrefix} 第 {iteration} 轮单帧误差明细 — ");
        for (int i = 0; i < perViewErrors.Length; i++)
        {
            int origIdx = keptIndices[i];
            Guid? photoId = canMapPhotos ? photos![origIdx].Id : null;
            if (i > 0)
                detailSb.Append("; ");
            detailSb.Append($"[{origIdx}]");
            if (photoId.HasValue)
                detailSb.Append($"PhotoId={photoId.Value:N}");
            detailSb.Append($"Err={perViewErrors[i]:F4}");
        }
        _logger.LogInformation(detailSb.ToString());

        // 按误差从大到小取 top 5
        var ordered = perViewErrors
            .Select(
                (err, keptIdx) =>
                    new
                    {
                        KeptIdx = keptIdx,
                        OrigIdx = keptIndices[keptIdx],
                        Err = err,
                    }
            )
            .OrderByDescending(x => x.Err)
            .Take(5)
            .ToList();

        StringBuilder topSb = new StringBuilder();
        topSb.Append($"{logPrefix} 第 {iteration} 轮 TOP5 最差帧 — ");
        for (int i = 0; i < ordered.Count; i++)
        {
            var item = ordered[i];
            Guid? photoId = canMapPhotos ? photos![item.OrigIdx].Id : null;
            string? blobKey = canMapPhotos ? photos![item.OrigIdx].BlobKey : null;
            if (i > 0)
                topSb.Append("; ");
            topSb.Append($"[{item.OrigIdx}]");
            if (photoId.HasValue)
                topSb.Append($"PhotoId={photoId.Value:N},");
            topSb.Append($"BlobKey={blobKey ?? "N/A"},Err={item.Err:F4}");
        }
        _logger.LogInformation(topSb.ToString());
    }

    /// <summary>
    /// 标定拍照专用帧抓取方法。
    /// 流程：读取并保存当前触发模式 → 切换到目标模式 →
    ///         StartCapture（获取单活锁）→ 必要时发软件触发 → GrabFrame → StopCapture（释放锁）→ 恢复触发模式。
    /// </summary>
    /// <param name="cameraDeviceId">相机设备 ID</param>
    /// <param name="targetTriggerMode">0 = 自由运行；1 = 标准触发（硬件 IO）；2 = 软件触发</param>
    /// <param name="imageRotationAngle">图像顺时针旋转角度（度，支持 0/90/180/270）</param>
    private async Task<byte[]> GrabCalibFrameRawAsync(
        Guid cameraDeviceId,
        int targetTriggerMode,
        int imageRotationAngle = 0
    )
    {
        CameraDevice camera = await _cameraDeviceRepository.GetAsync(cameraDeviceId);
        ITucamCameraService cameraProvider = ResolveTucamCamera(camera);
        int idx = ResolveRuntimeIndex(camera, cameraProvider);

        if (!_tucamService.IsCameraOpen(idx))
        {
            throw new UserFriendlyException(
                $"相机未打开（DeviceIndex={idx}），请先打开相机后再进行标定拍照"
            );
        }

        // 每相机串行化「读取→切换→恢复」触发模式：应用服务为 Transient，若两次标定拍照
        // 并发命中同一相机，后者可能把前者设置的目标模式误读为「原始模式」，导致恢复阶段
        // 把相机遗留在错误触发模式。故用进程级 static 每相机信号量把整段读改恢复串行化。
        SemaphoreSlim triggerLock = _triggerModeLocks.GetOrAdd(
            cameraDeviceId,
            static _ => new SemaphoreSlim(1, 1)
        );
        await triggerLock.WaitAsync();
        try
        {
            // 读取并保存原始触发模式（拍照完成后恢复）
            long originalTriggerMode = 0;
            try
            {
                originalTriggerMode = await _tucamService.GetGenICamIntAsync(idx, "TriggerMode");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "标定拍照：相机 {Index} 读取触发模式失败，按自由运行处理",
                    idx
                );
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
                        long exposureUs = await _tucamService.GetGenICamIntAsync(
                            idx,
                            "ExposureTime"
                        );
                        timeoutMs = Math.Max((int)(exposureUs / 1000L) * 2 + 1000, timeoutMs);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(
                            ex,
                            "标定拍照：相机 {Index} 读取曝光时间失败，使用默认超时 {TimeoutMs}ms",
                            idx,
                            timeoutMs
                        );
                    }

                    byte[] jpegBytes = await _tucamService.GrabFrameRawAsync(
                        idx,
                        timeoutMs,
                        imageRotationAngle: imageRotationAngle
                    );
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
                        await _tucamService.SetGenICamIntAsync(
                            idx,
                            "TriggerMode",
                            originalTriggerMode
                        );
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
        finally
        {
            triggerLock.Release();
        }
    }

    private Guid ValidateAndGetProjectorId(CalibProject project)
    {
        if (project.DeviceSeries == DeviceSeries.NoLight)
            throw new UserFriendlyException("无光系列不支持外参拍照");
        if (project.DeviceType == CalibDeviceType.TwoCamera1Light)
            throw new UserFriendlyException("双目结构光模式下不支持外参拍照");
        if (!project.BoundProjectorDeviceId.HasValue)
            throw new UserFriendlyException("请先在项目管理页绑定主结构光机后再执行外参拍照");
        return project.BoundProjectorDeviceId.Value;
    }

    /// <summary>构建标定照片的 BLOB Key</summary>
    private static string BuildBlobKey(
        Guid projectId,
        Guid cameraId,
        CalibPhotoType type,
        ExtrinsicPhotoPhase? extrinsicPhase = null,
        int? frameIndex = null
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
            ExtrinsicPhotoPhase.WhiteScreen => "_ws",
            ExtrinsicPhotoPhase.Checkerboard => "_cb",
            ExtrinsicPhotoPhase.ProjectorOff => "_off", // 历史遗留：旧投影仪外参流程
            ExtrinsicPhotoPhase.ProjectorOn => "_on", // 历史遗留：旧投影仪外参流程
            _ => string.Empty,
        };
        string frameSuffix = frameIndex.HasValue ? $"_f{frameIndex.Value:D3}" : string.Empty;
        return $"{projectId}/{cameraId}/{typeStr}/{ts}{phaseSuffix}{frameSuffix}.jpg";
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

    private static string BuildStereoMapBlobKey(Guid projectId, string mapName)
    {
        return $"stereo/{projectId}/{mapName}.bin";
    }

    private static byte[] SerializeFloatMapToBinary(Mat map)
    {
        int rows = map.Rows;
        int cols = map.Cols;
        byte[] result = new byte[rows * cols * sizeof(float)];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                float val = map.At<float>(r, c);
                int idx = (r * cols + c) * sizeof(float);
                BitConverter.TryWriteBytes(result.AsSpan(idx), val);
            }
        }
        return result;
    }

    private static CalibBoardConfigDto ToBoardConfigDto(CalibProject project)
    {
        CircleBoardConfigDto? circleConfig = null;
        if (project.BoardType != CalibrationBoardType.Chessboard)
        {
            circleConfig = new CircleBoardConfigDto
            {
                PatternSize = new CirclePatternSizeDto
                {
                    Width = project.CirclePatternCols ?? 0,
                    Height = project.CirclePatternRows ?? 0,
                },
                CircleSpacing = project.CircleSpacingMm ?? 0,
                CircleDiameter = project.CircleDiameterMm ?? 0,
                HasCenterMarker = project.HasCenterMarker ?? false,
                HasCornerLocators = project.HasCornerLocators ?? false,
                MarkerPosition = new CircleMarkerPositionDto
                {
                    Row = project.MarkerRow ?? 0,
                    Col = project.MarkerCol ?? 0,
                },
                Detector =
                    project.CircleDetectorConfigJson != null
                        ? JsonSerializer.Deserialize<CircleBlobDetectorConfigDto>(
                            project.CircleDetectorConfigJson,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                        )
                        : null,
            };
        }

        return new CalibBoardConfigDto
        {
            BoardType = project.BoardType,
            PhysicalCornerRows = project.PhysicalCornerRows,
            PhysicalCornerCols = project.PhysicalCornerCols,
            PhysicalSquareSizeMm = project.PhysicalSquareSizeMm,
            ProjectedCornerRows = project.ProjectedCornerRows,
            ProjectedCornerCols = project.ProjectedCornerCols,
            ProjectedPixelSize = project.ProjectedPixelSize,
            BoardThicknessMm = project.BoardThicknessMm,
            CircleBoardConfig = circleConfig,
        };
    }

    private static CalibPhotoDto ToPhotoDto(
        CalibPhotoRecord record,
        bool compressLegacyThumbnail = false
    )
    {
        string? thumbnail = record.ThumbnailBase64;
        if (
            compressLegacyThumbnail
            && !string.IsNullOrEmpty(thumbnail)
            && thumbnail.Length > 300_000
        )
        {
            int separator = thumbnail.IndexOf(',');
            string encoded = separator >= 0 ? thumbnail[(separator + 1)..] : thumbnail;
            try
            {
                thumbnail = CreateThumbnailBase64(Convert.FromBase64String(encoded));
            }
            catch (FormatException)
            {
                thumbnail = null;
            }
        }

        return new CalibPhotoDto
        {
            Id = record.Id,
            PhotoType = record.PhotoType,
            IsValid = record.IsValid,
            CornerCountDetected = record.CornerCountDetected,
            CapturedAt = record.CapturedAt,
            ThumbnailBase64 = thumbnail,
            BlobKey = record.BlobKey,
            PairGroupId = record.PairGroupId,
            StereoRole = record.StereoRole,
            ExtrinsicPhase = record.ExtrinsicPhase.HasValue
                ? (ExtrinsicPhotoPhaseDto)record.ExtrinsicPhase.Value
                : null,
            ImageDiffScore = record.ImageDiffScore,
            ImageDiffSignificant = record.ImageDiffSignificant,
        };
    }

    private ITucamCameraService ResolveTucamCamera(CameraDevice camera)
    {
        if ((camera.Capabilities & CameraCapability.Snapshot) == 0)
            throw new UserFriendlyException($"相机 [{camera.Name}] 不支持标定拍照");
        if (string.IsNullOrWhiteSpace(camera.HardwareId))
            throw new UserFriendlyException($"相机 [{camera.Name}] 尚未绑定稳定硬件标识");
        ICameraDriver driver = _cameraDrivers.GetRequired(camera.DriverId);
        return driver as ITucamCameraService
            ?? throw new UserFriendlyException(
                $"相机驱动 [{camera.DriverId}] 尚未提供标定参数适配器"
            );
    }

    private static int ResolveRuntimeIndex(
        CameraDevice camera,
        ITucamCameraService provider
    )
    {
        ICameraDriver driver = (ICameraDriver)provider;
        return driver.TryGetRuntimeIndex(camera.HardwareId!, out int index)
            ? index
            : throw new UserFriendlyException($"相机 [{camera.Name}] 当前离线");
    }

    private static string CreateThumbnailBase64(byte[] imageBytes)
    {
        using Mat source = Cv2.ImDecode(imageBytes, ImreadModes.Color);
        if (source.Empty())
            return string.Empty;

        const int maxSide = 480;
        double scale = Math.Min(1.0, (double)maxSide / Math.Max(source.Cols, source.Rows));
        using Mat thumbnail = new();
        if (scale < 1.0)
        {
            Cv2.Resize(
                source,
                thumbnail,
                new Size(
                    Math.Max(1, (int)Math.Round(source.Cols * scale)),
                    Math.Max(1, (int)Math.Round(source.Rows * scale))
                ),
                0,
                0,
                InterpolationFlags.Area
            );
        }
        else
        {
            source.CopyTo(thumbnail);
        }

        Cv2.ImEncode(
            ".jpg",
            thumbnail,
            out byte[] encoded,
            new ImageEncodingParam(ImwriteFlags.JpegQuality, 70)
        );
        return $"data:image/jpeg;base64,{Convert.ToBase64String(encoded)}";
    }

    private async Task<(
        List<Mat> objectMats,
        List<Mat> imageMats,
        Size imageSize
    )> CollectIntrinsicPointMatsAsync(
        List<CalibPhotoRecord> intrinsicPhotos,
        Point3f[] worldCorners,
        Size patternSize,
        CalibProject project,
        int rotationAngle,
        string? logPrefix = null
    )
    {
        List<Mat> objectMats = [];
        List<Mat> imageMats = [];
        Size imageSize = default;
        System.Diagnostics.Stopwatch? sw =
            logPrefix != null ? System.Diagnostics.Stopwatch.StartNew() : null;

        for (int idx = 0; idx < intrinsicPhotos.Count; idx++)
        {
            CalibPhotoRecord photo = intrinsicPhotos[idx];

            sw?.Restart();
            byte[] bytes = await _blobContainer.GetAllBytesAsync(photo.BlobKey);
            long blobMs = sw?.ElapsedMilliseconds ?? 0;

            sw?.Restart();
            // Blob 中的 BMP 已在拍照阶段（GrabFrameRawAsync → EncodeToBmp）应用过旋转，
            // 此处不再二次旋转，避免坐标系错乱导致标定误差激增。
            using Mat mat = CalibImageUtils.LoadGrayMat(bytes);
            long loadMs = sw?.ElapsedMilliseconds ?? 0;

            if (mat.Empty())
            {
                if (logPrefix != null)
                    _logger.LogWarning(
                        "[{Prefix}] [{Idx}/{Total}] 图像解码失败，跳过",
                        logPrefix,
                        idx + 1,
                        intrinsicPhotos.Count
                    );
                continue;
            }

            if (imageSize == default)
            {
                imageSize = new Size(mat.Cols, mat.Rows);
                if (logPrefix != null)
                    _logger.LogInformation(
                        "[{Prefix}] 图像分辨率 {W}x{H}",
                        logPrefix,
                        mat.Cols,
                        mat.Rows
                    );
            }

            sw?.Restart();
            Point2f[]? corners = _boardDetector.FindBoardPointsSubpixGray(
                mat,
                patternSize,
                project.BoardType,
                project
            );
            long cornerMs = sw?.ElapsedMilliseconds ?? 0;

            if (logPrefix != null)
                _logger.LogInformation(
                    "[{Prefix}] [{Idx}/{Total}] blob={BlobMs}ms 解码={LoadMs}ms 角点={CornerMs}ms {Result}",
                    logPrefix,
                    idx + 1,
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

        return (objectMats, imageMats, imageSize);
    }

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

    private static float GetBoardSpacingMm(CalibProject project)
    {
        if (project.BoardType == CalibrationBoardType.Chessboard)
        {
            return (float)project.PhysicalSquareSizeMm;
        }

        return (float)(project.CircleSpacingMm ?? 0);
    }

    private static Point3f[] BuildBoardWorldPoints(
        CalibProject project,
        Size patternSize,
        float spacingMm
    )
    {
        return CalibComputationUtils.BuildBoardWorldPoints(project, patternSize, spacingMm);
    }

    private static void EnsureBoardConfigValid(CalibProject project, bool isProjectedBoard)
    {
        if (isProjectedBoard)
        {
            if (project.ProjectedCornerRows <= 1 || project.ProjectedCornerCols <= 1)
                throw new UserFriendlyException("投影棋盘格行列数必须大于1");
        }
        else
        {
            if (project.BoardType == CalibrationBoardType.Chessboard)
            {
                if (project.PhysicalCornerRows <= 1 || project.PhysicalCornerCols <= 1)
                    throw new UserFriendlyException("物理标定板行列数必须大于1");
                if (project.PhysicalSquareSizeMm <= 0)
                    throw new UserFriendlyException("物理标定板格子尺寸必须大于0");
            }
            else
            {
                if (!project.CirclePatternRows.HasValue || project.CirclePatternRows.Value <= 1)
                    throw new UserFriendlyException("圆点板行数必须大于1");
                if (!project.CirclePatternCols.HasValue || project.CirclePatternCols.Value <= 1)
                    throw new UserFriendlyException("圆点板列数必须大于1");
                if (!project.CircleSpacingMm.HasValue || project.CircleSpacingMm.Value <= 0)
                    throw new UserFriendlyException("圆点中心间距必须大于0");
            }
        }
    }

    private static double GetMaxSingleCameraReprojectionError(CalibProject project)
    {
        return project.BoardType == CalibrationBoardType.Chessboard
            ? CalibConsts.MaxSingleCameraReprojectionError
            : CalibConsts.MaxCircleBoardReprojectionError;
    }

    /// <inheritdoc/>
    public async Task<bool> ValidateStep5Async(Guid calibProjectId)
    {
        CalibProject project = await _projectRepo.GetAsync(calibProjectId);

        List<Guid> cameraIds = new();
        if (project.MainCameraDeviceId.HasValue)
            cameraIds.Add(project.MainCameraDeviceId.Value);
        if (project.SecondaryCameraDeviceId.HasValue)
            cameraIds.Add(project.SecondaryCameraDeviceId.Value);

        if (cameraIds.Count == 0)
            return false;

        IQueryable<CalibCameraParam> paramQuery = await _cameraParamRepo.GetQueryableAsync();

        foreach (Guid cameraId in cameraIds)
        {
            CalibCameraParam? param = await AsyncExecuter.FirstOrDefaultAsync(
                paramQuery.Where(x =>
                    x.CalibProjectId == calibProjectId && x.CameraDeviceId == cameraId
                )
            );

            if (string.IsNullOrWhiteSpace(param?.IntrinsicMatrixJson))
                return false;

            if (project.DeviceSeries == DeviceSeries.SingleLight)
            {
                if (string.IsNullOrWhiteSpace(param.ExtrinsicRvecJson))
                    return false;
            }
        }

        if (project.SecondaryCameraDeviceId.HasValue)
        {
            IQueryable<CalibStereoResult> stereoQuery = await _stereoResultRepo.GetQueryableAsync();
            CalibStereoResult? stereoResult = await AsyncExecuter.FirstOrDefaultAsync(
                stereoQuery.Where(x => x.CalibProjectId == calibProjectId)
            );

            if (stereoResult == null || string.IsNullOrWhiteSpace(stereoResult.RotationMatrixJson))
                return false;
        }

        return true;
    }

    private static CalibStereoComputeResultDto ToStereoResultDto(CalibStereoResult result)
    {
        return new CalibStereoComputeResultDto
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
}
