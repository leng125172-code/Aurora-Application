using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AuroraStruct3D.Calibration.Dtos;
using AuroraStruct3D.Cameras;
using AuroraStruct3D.Cameras.Dtos;
using AuroraStruct3D.Motors;
using AuroraStruct3D.OpenCV.ImageOps;
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
        public required List<CalibPhotoRecord> ProjectorPatternPhotos { get; init; }

        public bool IsValid =>
            ProjectorPatternPhotos.Count > 0
            && ProjectorOffPhoto.IsValid
            && ProjectorOnPhoto.IsValid
            && (ProjectorOffPhoto.ImageDiffSignificant ?? true);
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
        // 获取项目棋盘格参数
        CalibProject project = await _projectRepo.GetAsync(input.CalibProjectId);
        CameraDevice camera = await _cameraDeviceRepository.GetAsync(input.CameraDeviceId);

        if (
            project.DeviceSeries == DeviceSeries.SingleLight
            && project.BoundProjectorDeviceId.HasValue
            && input.ProjectorDeviceId.HasValue
            && input.ProjectorDeviceId.Value != project.BoundProjectorDeviceId.Value
        )
        {
            throw new UserFriendlyException("当前项目绑定的投影仪与拍照参数不一致，请刷新后重试");
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
            isProjectedBoard: false,
            camera.ImageRotationAngle
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
        CameraDevice camera = await _cameraDeviceRepository.GetAsync(input.CameraDeviceId);

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

        byte[] whiteScreenBytes = null!;
        byte[] checkerboardBytes = null!;
        try
        {
            await _projectorService.LedOnAsync(projectorDeviceId);

            // 步骤 1：S1 白屏模式拍摄圆点标定板
            await _projectorService.SetDisplayModeAsync(
                new SetProjectorDisplayModeDto
                {
                    ProjectorDeviceId = projectorDeviceId,
                    Mode = ProjectorDisplayMode.White,
                }
            );
            await Task.Delay(200);
            whiteScreenBytes = await GrabCalibFrameRawAsync(
                input.CameraDeviceId,
                targetTriggerMode: 2
            );

            // 步骤 2：S3 棋盘格模式拍摄投影棋盘格+圆点标定板组合场景
            // 先设置棋盘格像素尺寸（S11），再切换到棋盘格模式（S3）
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
            checkerboardBytes = await GrabCalibFrameRawAsync(
                input.CameraDeviceId,
                targetTriggerMode: 2
            );
        }
        finally
        {
            await _projectorService.LedOffAsync(projectorDeviceId);
        }

        // 步骤 3：图像差分验证两次拍摄的差异性
        ImageDiffResult diffResult = ImageDiffAnalyzer.ComputeDiff(
            whiteScreenBytes,
            checkerboardBytes
        );

        // 步骤 4：角点检测验证标定板有效性
        (bool whiteScreenValid, int whiteScreenCornerCount) = DetectBoardFeaturePoints(
            whiteScreenBytes,
            project,
            isProjectedBoard: false,
            camera.ImageRotationAngle
        );

        (bool checkerboardValid, int checkerboardCornerCount) = DetectBoardFeaturePoints(
            checkerboardBytes,
            project,
            isProjectedBoard: true,
            camera.ImageRotationAngle
        );

        // 判断样本有效性：差分显著且两张照片都检测到有效角点
        bool isSampleValid = diffResult.IsSignificant && whiteScreenValid && checkerboardValid;

        // 保存白屏照片
        string whiteScreenBlobKey = BuildBlobKey(
            input.CalibProjectId,
            input.CameraDeviceId,
            CalibPhotoType.Extrinsic,
            ExtrinsicPhotoPhase.WhiteScreen,
            frameIndex: 0
        );
        await _blobContainer.SaveAsync(
            whiteScreenBlobKey,
            whiteScreenBytes,
            overrideExisting: false
        );

        CalibPhotoRecord whiteScreenRecord = new(
            GuidGenerator.Create(),
            input.CalibProjectId,
            input.CameraDeviceId,
            CalibPhotoType.Extrinsic,
            whiteScreenBlobKey,
            whiteScreenValid,
            whiteScreenCornerCount,
            GenerateThumbnailBase64(whiteScreenBytes),
            pairGroupId,
            stereoRole: null,
            extrinsicPhase: ExtrinsicPhotoPhase.WhiteScreen,
            imageDiffScore: diffResult.DiffScore,
            imageDiffSignificant: diffResult.IsSignificant
        );
        await _photoRepo.InsertAsync(whiteScreenRecord);

        // 保存棋盘格照片
        string checkerboardBlobKey = BuildBlobKey(
            input.CalibProjectId,
            input.CameraDeviceId,
            CalibPhotoType.Extrinsic,
            ExtrinsicPhotoPhase.Checkerboard,
            frameIndex: 0
        );
        await _blobContainer.SaveAsync(
            checkerboardBlobKey,
            checkerboardBytes,
            overrideExisting: false
        );

        CalibPhotoRecord checkerboardRecord = new(
            GuidGenerator.Create(),
            input.CalibProjectId,
            input.CameraDeviceId,
            CalibPhotoType.Extrinsic,
            checkerboardBlobKey,
            checkerboardValid,
            checkerboardCornerCount,
            GenerateThumbnailBase64(checkerboardBytes),
            pairGroupId,
            stereoRole: null,
            extrinsicPhase: ExtrinsicPhotoPhase.Checkerboard,
            imageDiffScore: diffResult.DiffScore,
            imageDiffSignificant: diffResult.IsSignificant
        );
        await _photoRepo.InsertAsync(checkerboardRecord);

        _logger.LogInformation(
            "[外参拍照] 样本 {PairGroupId} 完成：差分分数={DiffScore:F4}, 差分显著={DiffSignificant}, 白屏角点={WhiteCorners}, 棋盘格角点={CheckerboardCorners}, 有效={IsValid}",
            pairGroupId,
            diffResult.DiffScore,
            diffResult.IsSignificant,
            whiteScreenCornerCount,
            checkerboardCornerCount,
            isSampleValid
        );

        return new CalibExtrinsicSampleDto
        {
            PairGroupId = pairGroupId,
            ProjectorOffPhoto = ToPhotoDto(whiteScreenRecord),
            ProjectorOnPhoto = ToPhotoDto(checkerboardRecord),
            IsValid = isSampleValid,
        };
    }

    /// <inheritdoc/>
    public async Task<CalibPhotoDto> TakeExtrinsicDotPhotoAsync(TakeExtrinsicPhotoInput input)
    {
        CalibProject project = await _projectRepo.GetAsync(input.CalibProjectId);
        CameraDevice camera = await _cameraDeviceRepository.GetAsync(input.CameraDeviceId);

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
            photoBytes = await GrabCalibFrameRawAsync(input.CameraDeviceId, targetTriggerMode: 2);
        }
        finally
        {
            await _projectorService.LedOffAsync(projectorDeviceId);
        }

        (bool isValid, int cornerCount) = DetectBoardFeaturePoints(
            photoBytes,
            project,
            isProjectedBoard: false,
            camera.ImageRotationAngle
        );

        string blobKey = BuildBlobKey(
            input.CalibProjectId,
            input.CameraDeviceId,
            CalibPhotoType.Extrinsic,
            ExtrinsicPhotoPhase.WhiteScreen,
            frameIndex: 0
        );
        await _blobContainer.SaveAsync(blobKey, photoBytes, overrideExisting: false);

        CalibPhotoRecord record = new(
            GuidGenerator.Create(),
            input.CalibProjectId,
            input.CameraDeviceId,
            CalibPhotoType.Extrinsic,
            blobKey,
            isValid,
            cornerCount,
            GenerateThumbnailBase64(photoBytes),
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

        Guid? pairGroupId = null;
        try
        {
            IQueryable<CalibPhotoRecord> query = await _photoRepo.GetQueryableAsync();
            CalibPhotoRecord? latestDotPhoto = await AsyncExecuter.FirstOrDefaultAsync(
                query
                    .Where(x =>
                        x.CalibProjectId == input.CalibProjectId
                        && x.CameraDeviceId == input.CameraDeviceId
                        && x.PhotoType == CalibPhotoType.Extrinsic
                        && x.ExtrinsicPhase == ExtrinsicPhotoPhase.WhiteScreen
                        && !query.Any(q =>
                            q.PairGroupId == x.PairGroupId
                            && q.ExtrinsicPhase == ExtrinsicPhotoPhase.Checkerboard
                        )
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
        catch
        {
            pairGroupId = GuidGenerator.Create();
        }

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
            photoBytes = await GrabCalibFrameRawAsync(input.CameraDeviceId, targetTriggerMode: 2);
        }
        finally
        {
            await _projectorService.LedOffAsync(projectorDeviceId);
        }

        CameraDevice camera = await _cameraDeviceRepository.GetAsync(input.CameraDeviceId);
        (bool isValid, int cornerCount) = DetectBoardFeaturePoints(
            photoBytes,
            project,
            isProjectedBoard: true,
            camera.ImageRotationAngle
        );

        string blobKey = BuildBlobKey(
            input.CalibProjectId,
            input.CameraDeviceId,
            CalibPhotoType.Extrinsic,
            ExtrinsicPhotoPhase.Checkerboard,
            frameIndex: 0
        );
        await _blobContainer.SaveAsync(blobKey, photoBytes, overrideExisting: false);

        CalibPhotoRecord record = new(
            GuidGenerator.Create(),
            input.CalibProjectId,
            input.CameraDeviceId,
            CalibPhotoType.Extrinsic,
            blobKey,
            isValid,
            cornerCount,
            GenerateThumbnailBase64(photoBytes),
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
        byte[] mainBytes = await GrabCalibFrameRawAsync(mainCameraId, targetTriggerMode: 2);
        byte[] secondaryBytes = await GrabCalibFrameRawAsync(
            secondaryCameraId,
            targetTriggerMode: 2
        );

        (bool mainValid, int mainCornerCount) = DetectBoardFeaturePoints(
            mainBytes,
            project,
            isProjectedBoard: false,
            mainCamera.ImageRotationAngle
        );
        (bool secondaryValid, int secondaryCornerCount) = DetectBoardFeaturePoints(
            secondaryBytes,
            project,
            isProjectedBoard: false,
            secondaryCamera.ImageRotationAngle
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

        List<Mat> objectMats = [];
        List<Mat> imageMats = [];
        Size imageSize = default;

        for (int idx = 0; idx < intrinsicPhotos.Count; idx++)
        {
            CalibPhotoRecord photo = intrinsicPhotos[idx];
            byte[] bytes = await _blobContainer.GetAllBytesAsync(photo.BlobKey);
            using Mat mat = LoadGrayMatWithRotation(bytes, camera.ImageRotationAngle);
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
            project,
            camera.ImageRotationAngle
        );

        string? rvecJson = null;
        string? tvecJson = null;
        double? projectorReprojectionError = null;

        if (bestExtrinsic != null)
        {
            Point2f[]? exCorners = await TryFindPhysicalBoardCornersFromSampleAsync(
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
                rvecJson = SerializeVecToJson(rvec);
                tvecJson = SerializeVecToJson(tvec);
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
                Point3f[]? objPts = await BuildProjectorObjectPointsCamAsync(
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
            using Mat mat = LoadGrayMatWithRotation(bytes, camera.ImageRotationAngle);
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
                Point2f[]? exCorners = await TryFindPhysicalBoardCornersFromSampleAsync(
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

            // 直接加载为灰度（计算路径不需要彩色数据），并应用相机旋转角度
            phaseSwS.Restart();
            using Mat mainMat = LoadGrayMatWithRotation(mainBytes, mainCamera.ImageRotationAngle);
            using Mat secondaryMat = LoadGrayMatWithRotation(
                secondaryBytes,
                secondaryCamera.ImageRotationAngle
            );
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
            ExtrinsicPhotoPhase.ProjectorOff => "_off",
            ExtrinsicPhotoPhase.ProjectorOn => "_on",
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

    private static List<ProjectorExtrinsicSampleGroup> BuildExtrinsicSampleGroups(
        IEnumerable<CalibPhotoRecord> photos
    )
    {
        return photos
            .Where(x => x.PairGroupId.HasValue)
            .GroupBy(x => x.PairGroupId!.Value)
            .Select(group =>
            {
                CalibPhotoRecord? projectorOff =
                    group.FirstOrDefault(x => x.ExtrinsicPhase == ExtrinsicPhotoPhase.ProjectorOff)
                    ?? group.FirstOrDefault(x =>
                        x.ExtrinsicPhase == ExtrinsicPhotoPhase.WhiteScreen
                    );

                List<CalibPhotoRecord> projectorOnPhotos = group
                    .Where(x =>
                        x.ExtrinsicPhase == ExtrinsicPhotoPhase.ProjectorOn
                        || x.ExtrinsicPhase == ExtrinsicPhotoPhase.Checkerboard
                    )
                    .OrderBy(x => ExtractFrameIndexFromBlobKey(x.BlobKey) ?? int.MaxValue)
                    .ThenBy(x => x.CapturedAt)
                    .ToList();
                CalibPhotoRecord? projectorOn = projectorOnPhotos.LastOrDefault();

                return projectorOff == null || projectorOn == null
                    ? null
                    : new ProjectorExtrinsicSampleGroup
                    {
                        PairGroupId = group.Key,
                        ProjectorOffPhoto = projectorOff,
                        ProjectorOnPhoto = projectorOn,
                        ProjectorPatternPhotos = projectorOnPhotos,
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

    private static int? ExtractFrameIndexFromBlobKey(string blobKey)
    {
        int suffixStart = blobKey.LastIndexOf("_f", StringComparison.OrdinalIgnoreCase);
        int suffixEnd = blobKey.LastIndexOf(".jpg", StringComparison.OrdinalIgnoreCase);
        if (suffixStart < 0 || suffixEnd <= suffixStart + 2)
        {
            return null;
        }

        ReadOnlySpan<char> digits = blobKey.AsSpan(suffixStart + 2, suffixEnd - (suffixStart + 2));
        return int.TryParse(digits, out int frameIndex) ? frameIndex : null;
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
        bool isProjectedBoard,
        int rotationAngle = 0
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
                using Mat projGray = LoadGrayMatWithRotation(imageBytes, rotationAngle);
                if (projGray.Empty())
                    return (false, 0);

                // 提取投影有效区域，减少背景干扰（阈值80，分析显示此阈值效果最好）
                using Mat projMask = new();
                Cv2.Threshold(projGray, projMask, 80, 255, ThresholdTypes.Binary);
                using Mat maskClosed = new();
                Cv2.MorphologyEx(
                    projMask,
                    maskClosed,
                    MorphTypes.Close,
                    Cv2.GetStructuringElement(MorphShapes.Rect, new Size(31, 31))
                );
                using Mat maskOpen = new();
                Cv2.MorphologyEx(
                    maskClosed,
                    maskOpen,
                    MorphTypes.Open,
                    Cv2.GetStructuringElement(MorphShapes.Rect, new Size(31, 31))
                );

                using Mat maskedGray = new();
                projGray.CopyTo(maskedGray, maskOpen);

                _logger.LogInformation(
                    "投影棋盘格ROI提取: 原图={W}x{H}",
                    projGray.Cols,
                    projGray.Rows
                );

                // 策略1: 直接对masked原图调用FindCornersSubpixGray（包含SB算法）
                Point2f[]? corners1 = FindCornersSubpixGray(maskedGray, chess);
                if (corners1 != null)
                {
                    _logger.LogInformation(
                        "投影棋盘格检测成功[原图+SB]: Pattern={Cols}x{Rows}, 角点数={Count}",
                        chess.Width,
                        chess.Height,
                        corners1.Length
                    );
                    return (true, corners1.Length);
                }

                // 策略2: CLAHE增强后调用
                using CLAHE clahe = Cv2.CreateCLAHE(clipLimit: 2.0, tileGridSize: new Size(8, 8));
                using Mat claheMat = new();
                clahe.Apply(maskedGray, claheMat);
                Point2f[]? corners2 = FindCornersSubpixGray(claheMat, chess);
                if (corners2 != null)
                {
                    _logger.LogInformation(
                        "投影棋盘格检测成功[CLAHE+SB]: Pattern={Cols}x{Rows}, 角点数={Count}",
                        chess.Width,
                        chess.Height,
                        corners2.Length
                    );
                    return (true, corners2.Length);
                }

                // 策略3: 高斯模糊后调用（减少噪声干扰）
                using Mat blurred = new();
                Cv2.GaussianBlur(maskedGray, blurred, new Size(3, 3), 0);
                Point2f[]? corners3 = FindCornersSubpixGray(blurred, chess);
                if (corners3 != null)
                {
                    _logger.LogInformation(
                        "投影棋盘格检测成功[高斯模糊+SB]: Pattern={Cols}x{Rows}, 角点数={Count}",
                        chess.Width,
                        chess.Height,
                        corners3.Length
                    );
                    return (true, corners3.Length);
                }

                // 策略4: 直接对原图（不做ROI裁剪）调用SB算法
                try
                {
                    if (
                        Cv2.FindChessboardCornersSB(projGray, chess, out Point2f[] sbCorners)
                        && sbCorners.Length > 0
                    )
                    {
                        _logger.LogInformation(
                            "投影棋盘格检测成功[SB原图]: Pattern={Cols}x{Rows}, 角点数={Count}",
                            chess.Width,
                            chess.Height,
                            sbCorners.Length
                        );

                        // SB算法精度对比：保存SB原始坐标
                        Point2f[] sbOriginal = (Point2f[])sbCorners.Clone();

                        // 亚像素精化
                        Cv2.CornerSubPix(
                            projGray,
                            sbCorners,
                            new Size(11, 11),
                            new Size(-1, -1),
                            new TermCriteria(CriteriaTypes.Eps | CriteriaTypes.MaxIter, 30, 0.001)
                        );

                        // 计算差异
                        double maxDiff = 0;
                        double avgDiff = 0;
                        for (int i = 0; i < sbCorners.Length; i++)
                        {
                            double dx = sbCorners[i].X - sbOriginal[i].X;
                            double dy = sbCorners[i].Y - sbOriginal[i].Y;
                            double dist = Math.Sqrt(dx * dx + dy * dy);
                            maxDiff = Math.Max(maxDiff, dist);
                            avgDiff += dist;
                        }
                        avgDiff /= sbCorners.Length;
                        _logger.LogInformation(
                            "[SB精度对比] 投影棋盘格 — SB原始坐标与亚像素精化后差异: 平均={Avg:F4}px, 最大={Max:F4}px",
                            avgDiff,
                            maxDiff
                        );

                        return (true, sbCorners.Length);
                    }
                }
                catch { }

                _logger.LogWarning(
                    "投影棋盘格检测失败(所有策略均失败): Pattern={Cols}x{Rows}, Image={W}x{H}",
                    chess.Width,
                    chess.Height,
                    projGray.Cols,
                    projGray.Rows
                );
                return (false, 0);
            }

            return DetectChessboardCorners(imageBytes, chess.Width, chess.Height, rotationAngle);
        }

        try
        {
            Size patternSize = GetBoardPatternSize(project, isProjectedBoard);
            using Mat gray = LoadGrayMatWithRotation(imageBytes, rotationAngle);
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
                return (false, 0);
            }

            int expectedCount = patternSize.Width * patternSize.Height;
            if (project.BoardType == CalibrationBoardType.MarkedSymmetricCircleGrid)
            {
                expectedCount--;
            }
            if (points.Length != expectedCount)
            {
                _logger.LogWarning(
                    "圆点板点数不完整: BoardType={BoardType}, Pattern={Cols}x{Rows}, Expected={Expected}, Actual={Actual}",
                    detectionBoardType,
                    patternSize.Width,
                    patternSize.Height,
                    expectedCount,
                    points.Length
                );
                return (false, points.Length);
            }

            return (true, points.Length);
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
        bool hasMarkerHole = boardType == CalibrationBoardType.MarkedSymmetricCircleGrid;
        int expectedCount = patternSize.Width * patternSize.Height - (hasMarkerHole ? 1 : 0);

        int markerRow = project?.MarkerRow ?? ((patternSize.Height - 1) / 2);
        int markerCol = project?.MarkerCol ?? ((patternSize.Width - 1) / 2);

        Point2f[]? TryDetect(Mat inputGray, string preprocessingName)
        {
            using Feature2D detector = CreateCircleBlobDetector(
                detectorConfig,
                new Size(inputGray.Cols, inputGray.Rows)
            );

            if (hasMarkerHole)
            {
                KeyPoint[] markedKeypoints = detector.Detect(inputGray);
                if (markedKeypoints.Length == 0)
                    return null;

                Point2f[] markedCenters = markedKeypoints
                    .Select(x => new Point2f(x.Pt.X, x.Pt.Y))
                    .ToArray();

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

                if (markedOk && orderedMarked != null && orderedMarked.Length == expectedCount)
                {
                    _logger.LogInformation(
                        "圆点板检测成功[{Preprocessing}]: BoardType={BoardType}, Pattern={Cols}x{Rows}, 点数={Count}",
                        preprocessingName,
                        boardType,
                        patternSize.Width,
                        patternSize.Height,
                        orderedMarked.Length
                    );
                    return orderedMarked;
                }

                return null;
            }
            else
            {
                FindCirclesGridFlags flags =
                    boardType == CalibrationBoardType.AsymmetricCircleGrid
                        ? FindCirclesGridFlags.AsymmetricGrid | FindCirclesGridFlags.Clustering
                        : FindCirclesGridFlags.SymmetricGrid | FindCirclesGridFlags.Clustering;

                if (
                    Cv2.FindCirclesGrid(
                        inputGray,
                        patternSize,
                        out Point2f[] centers,
                        flags,
                        detector
                    )
                )
                {
                    _logger.LogInformation(
                        "圆点板检测成功[{Preprocessing}]: BoardType={BoardType}, Pattern={Cols}x{Rows}, 点数={Count}, Method=OpenCV",
                        preprocessingName,
                        boardType,
                        patternSize.Width,
                        patternSize.Height,
                        centers.Length
                    );
                    return centers;
                }

                KeyPoint[] keypoints = detector.Detect(inputGray);
                if (keypoints.Length == 0)
                    return null;

                Point2f[] blobCenters = keypoints
                    .Select(x => new Point2f(x.Pt.X, x.Pt.Y))
                    .ToArray();

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

                if (orderedOk && ordered != null && ordered.Length == expectedCount)
                {
                    _logger.LogInformation(
                        "圆点板检测成功[{Preprocessing}]: BoardType={BoardType}, Pattern={Cols}x{Rows}, 点数={Count}, Method=CustomSort",
                        preprocessingName,
                        boardType,
                        patternSize.Width,
                        patternSize.Height,
                        ordered.Length
                    );
                    return ordered;
                }

                return null;
            }
        }

        Point2f[]? result;

        result = TryDetect(gray, "原图");
        if (result != null)
            return result;

        using Mat claheMat = new();
        using CLAHE clahe = Cv2.CreateCLAHE(clipLimit: 2.0, tileGridSize: new Size(8, 8));
        clahe.Apply(gray, claheMat);
        result = TryDetect(claheMat, "CLAHE");
        if (result != null)
            return result;

        using Mat blurredMat = new();
        Cv2.GaussianBlur(gray, blurredMat, new Size(3, 3), 0);
        result = TryDetect(blurredMat, "高斯模糊");
        if (result != null)
            return result;

        using Mat eqMat = new();
        Cv2.EqualizeHist(gray, eqMat);
        result = TryDetect(eqMat, "直方图均衡化");
        if (result != null)
            return result;

        _logger.LogWarning(
            "圆点板检测失败(所有策略均失败): BoardType={BoardType}, Pattern={Cols}x{Rows}, Image={W}x{H}",
            boardType,
            patternSize.Width,
            patternSize.Height,
            gray.Cols,
            gray.Rows
        );
        return null;
    }

    private static Feature2D CreateCircleBlobDetector(
        CircleBlobDetectorConfigDto cfg,
        Size? imageSize = null
    )
    {
        float minArea = (float)cfg.MinArea;
        float maxArea = (float)cfg.MaxArea;

        if (imageSize.HasValue && imageSize.Value.Width > 0 && imageSize.Value.Height > 0)
        {
            double imagePixels = imageSize.Value.Width * imageSize.Value.Height;
            double scaleFactor = imagePixels / (2448.0 * 2048.0);

            if (scaleFactor > 0)
            {
                minArea = (float)(cfg.MinArea * scaleFactor);
                maxArea = (float)(cfg.MaxArea * scaleFactor);
            }
        }

        var p = new SimpleBlobDetector.Params
        {
            MinThreshold = (float)cfg.MinThreshold,
            MaxThreshold = (float)cfg.MaxThreshold,
            FilterByArea = true,
            MinArea = minArea,
            MaxArea = maxArea,
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

    private static List<(string name, Mat image)> GenerateProjectedChessboardVariants(Mat gray)
    {
        List<(string, Mat)> variants = [];

        variants.Add(("原图", gray.Clone()));

        using CLAHE clahe = Cv2.CreateCLAHE(clipLimit: 2.0, tileGridSize: new Size(8, 8));
        using Mat claheMat = new();
        clahe.Apply(gray, claheMat);
        variants.Add(("CLAHE", claheMat.Clone()));

        using Mat equalized = new();
        Cv2.EqualizeHist(gray, equalized);
        variants.Add(("均衡化", equalized.Clone()));

        using Mat blurred = new();
        Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);
        variants.Add(("高斯模糊", blurred.Clone()));

        using Mat adaptiveThresh = new();
        Cv2.AdaptiveThreshold(
            gray,
            adaptiveThresh,
            255,
            AdaptiveThresholdTypes.GaussianC,
            ThresholdTypes.Binary,
            15,
            2
        );
        variants.Add(("自适应阈值", adaptiveThresh.Clone()));

        using Mat otsuThresh = new();
        Cv2.Threshold(gray, otsuThresh, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
        variants.Add(("Otsu二值化", otsuThresh.Clone()));

        using Mat claheAdaptive = new();
        Cv2.AdaptiveThreshold(
            claheMat,
            claheAdaptive,
            255,
            AdaptiveThresholdTypes.GaussianC,
            ThresholdTypes.Binary,
            15,
            2
        );
        variants.Add(("CLAHE+自适应", claheAdaptive.Clone()));

        using Mat claheOtsu = new();
        Cv2.Threshold(claheMat, claheOtsu, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
        variants.Add(("CLAHE+Otsu", claheOtsu.Clone()));

        return variants;
    }

    /// <summary>OpenCV 棋盘格角点检测（使用亚像素精化）。</summary>
    private (bool isValid, int cornerCount) DetectChessboardCorners(
        byte[] imageBytes,
        int cols,
        int rows,
        int rotationAngle = 0
    )
    {
        try
        {
            Size patternSize = new(cols, rows);

            // 优先尝试 OpenCV 直接解码
            using Mat mat = Cv2.ImDecode(imageBytes, ImreadModes.Color);
            if (!mat.Empty())
            {
                using Mat rotatedMat = ApplyRotation(mat, rotationAngle);
                // 先做一次降采样快速预检：若明确没有棋盘格特征，直接返回失败
                if (!HasChessboardFeatureQuickCheck(rotatedMat, patternSize))
                {
                    _logger.LogDebug(
                        "棋盘格快速预检未通过 (OpenCV解码路径): {Cols}x{Rows}，图像大小 {W}x{H}",
                        cols,
                        rows,
                        rotatedMat.Cols,
                        rotatedMat.Rows
                    );
                    return (false, 0);
                }

                _logger.LogDebug(
                    "棋盘格检测: OpenCV 解码成功 {W}x{H}，目标格式 {Cols}x{Rows}",
                    rotatedMat.Cols,
                    rotatedMat.Rows,
                    cols,
                    rows
                );
                Point2f[]? corners = FindCornersSubpix(rotatedMat, patternSize);
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
                    rotatedMat.Cols,
                    rotatedMat.Rows
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

            using Mat rotatedSkMat = ApplyRotation(skMat, rotationAngle);
            // 先做一次降采样快速预检：若明确没有棋盘格特征，直接返回失败
            if (!HasChessboardFeatureQuickCheck(rotatedSkMat, patternSize))
            {
                _logger.LogDebug(
                    "棋盘格快速预检未通过 (SkiaSharp解码路径): {Cols}x{Rows}，图像大小 {W}x{H}",
                    cols,
                    rows,
                    rotatedSkMat.Cols,
                    rotatedSkMat.Rows
                );
                return (false, 0);
            }

            _logger.LogDebug(
                "棋盘格检测: SkiaSharp 解码成功 {W}x{H}，目标格式 {Cols}x{Rows}",
                rotatedSkMat.Cols,
                rotatedSkMat.Rows,
                cols,
                rows
            );
            Point2f[]? skCorners = FindCornersSubpix(rotatedSkMat, patternSize);
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
                rotatedSkMat.Cols,
                rotatedSkMat.Rows
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

        using CLAHE clahe = Cv2.CreateCLAHE(clipLimit: 2.0, tileGridSize: new Size(8, 8));
        using Mat enhanced = new();
        clahe.Apply(grayFull, enhanced);

        // ── 步骤一：50% 缩放图检测（速度约 4× 快）───────────────────────────────
        int hw = Math.Max(1, grayFull.Cols / 2);
        int hh = Math.Max(1, grayFull.Rows / 2);
        using Mat grayHalf = new();
        Cv2.Resize(grayFull, grayHalf, new Size(hw, hh), 0, 0, InterpolationFlags.Area);

        using Mat enhancedHalf = new();
        Cv2.Resize(enhanced, enhancedHalf, new Size(hw, hh), 0, 0, InterpolationFlags.Area);

        Point2f[]? halfFound = null;
        string halfStrategy = "";
        Point2f[]? halfSbCorners = null;
        if (
            Cv2.FindChessboardCorners(grayHalf, patternSize, out Point2f[] c1h, flags1)
            && c1h.Length > 0
        )
        {
            halfFound = c1h;
            halfStrategy = "half-常规-flags1";
        }
        else if (
            Cv2.FindChessboardCorners(grayHalf, patternSize, out Point2f[] c2h, flags2)
            && c2h.Length > 0
        )
        {
            halfFound = c2h;
            halfStrategy = "half-常规-flags2";
        }
        else if (
            Cv2.FindChessboardCornersSB(grayHalf, patternSize, out Point2f[] c3h)
            && c3h.Length > 0
        )
        {
            halfSbCorners = c3h;
            halfFound = c3h;
            halfStrategy = "half-SB";
        }
        else if (
            Cv2.FindChessboardCorners(enhancedHalf, patternSize, out Point2f[] c4h, flags1)
            && c4h.Length > 0
        )
        {
            halfFound = c4h;
            halfStrategy = "half-增强-flags1";
        }
        else if (
            Cv2.FindChessboardCorners(enhancedHalf, patternSize, out Point2f[] c5h, flags2)
            && c5h.Length > 0
        )
        {
            halfFound = c5h;
            halfStrategy = "half-增强-flags2";
        }

        if (halfFound != null)
        {
            for (int i = 0; i < halfFound.Length; i++)
                halfFound[i] = new Point2f(halfFound[i].X * 2f, halfFound[i].Y * 2f);
            Cv2.CornerSubPix(grayFull, halfFound, new Size(11, 11), new Size(-1, -1), subPixCrit);
            return halfFound;
        }

        // ── 步骤二：全分辨率兜底（策略 1+2 + 增强图）──────────
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
        if (
            Cv2.FindChessboardCorners(enhanced, patternSize, out Point2f[] cf3, flags1)
            && cf3.Length > 0
        )
        {
            Cv2.CornerSubPix(grayFull, cf3, new Size(11, 11), new Size(-1, -1), subPixCrit);
            return cf3;
        }
        if (
            Cv2.FindChessboardCorners(enhanced, patternSize, out Point2f[] cf4, flags2)
            && cf4.Length > 0
        )
        {
            Cv2.CornerSubPix(grayFull, cf4, new Size(11, 11), new Size(-1, -1), subPixCrit);
            return cf4;
        }

        // ── 步骤三：全分辨率 SB 算法（对投影棋盘格最有效，分析显示只有 SB 能检测 15×8）───
        try
        {
            if (
                Cv2.FindChessboardCornersSB(grayFull, patternSize, out Point2f[] cf5)
                && cf5.Length > 0
            )
            {
                return cf5;
            }
            if (
                Cv2.FindChessboardCornersSB(enhanced, patternSize, out Point2f[] cf6)
                && cf6.Length > 0
            )
            {
                return cf6;
            }
        }
        catch { }

        return null;
    }

    /// <summary>生成缩略图 Base64（使用原图尺寸）——优先 SkiaSharp，避免 OpenCV ARM64 JPEG 兼容问题</summary>
    private static string? GenerateThumbnailBase64(byte[] imageBytes)
    {
        try
        {
            using SKBitmap? bitmap = SKBitmap.Decode(imageBytes);
            if (bitmap == null || bitmap.IsNull)
                return null;

            using SKImage skImg = SKImage.FromBitmap(bitmap);
            using SKData? encoded = skImg.Encode(SKEncodedImageFormat.Jpeg, 95);
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
        int expectedProjCornerCount,
        int rotationAngle = 0
    )
    {
        // 1. 先在整组样本里寻找可用于实体板位姿解算的角点（兼容条纹序列下 off 帧不可用）
        Point2f[]? boardCorners = await TryFindPhysicalBoardCornersFromSampleAsync(
            sample,
            physicalPatternSize,
            project,
            rotationAngle
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

        // 2. 图案帧 → 多帧检测并融合投影角点（中位数融合，抑制单帧抖动）
        List<Point2f[]> detectedCornerSets = [];
        foreach (CalibPhotoRecord patternPhoto in sample.ProjectorPatternPhotos)
        {
            byte[] onBytes = await _blobContainer.GetAllBytesAsync(patternPhoto.BlobKey);
            using Mat onGray = LoadGrayMatWithRotation(onBytes, rotationAngle);
            if (onGray.Empty())
            {
                continue;
            }

            Point2f[]? detectedCorners = FindBoardPointsSubpixGray(
                onGray,
                projPatternSize,
                GetDetectionBoardType(project, isProjectedBoard: true),
                project
            );

            if (detectedCorners != null && detectedCorners.Length == expectedProjCornerCount)
            {
                detectedCornerSets.Add(detectedCorners);
            }
        }

        if (detectedCornerSets.Count == 0)
            return null;

        Point2f[] patternCorners;
        if (detectedCornerSets.Count == 1)
        {
            patternCorners = detectedCornerSets[0];
        }
        else
        {
            patternCorners = FuseCornersRobust(detectedCornerSets, expectedProjCornerCount);
        }

        // 3. 去畸变到归一化相机坐标（作为光线方向），与标定板平面求交得到度量 3D
        using Mat undistMat = new();
        Cv2.UndistortPoints(InputArray.Create(patternCorners), undistMat, cameraMatrix, distCoeffs);
        undistMat.GetArray(out Point2f[] undist);
        if (undist.Length != patternCorners.Length)
            return null;

        double boardThickness = (double)project.BoardThicknessMm;
        _logger.LogInformation(
            "[外参标定] 标定板厚度补偿 — BoardThicknessMm={BoardThickness}, 平面偏移前 nDotP0={NDotP0}",
            boardThickness,
            nDotP0
        );

        Point3f[] objPts = new Point3f[patternCorners.Length];
        bool loggedFirstPoint = false;
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

            double px = tRay * dx;
            double py = tRay * dy;
            double pz = tRay * dz;

            if (boardThickness > 0)
            {
                double compensation = boardThickness / nDotD;
                px += compensation * dx;
                py += compensation * dy;
                pz += compensation * dz;
            }

            if (!loggedFirstPoint)
            {
                double rayLen = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                double cosTheta = nDotD / rayLen;
                _logger.LogInformation(
                    "[外参标定] 补偿后第一点 — tRay={TRay}, 3D点=({Px},{Py},{Pz}), cosTheta={CosTheta}",
                    tRay,
                    px,
                    py,
                    pz,
                    cosTheta
                );
                loggedFirstPoint = true;
            }

            objPts[i] = new Point3f((float)px, (float)py, (float)pz);
        }

        return objPts;
    }

    private static Point2f[] FuseCornersByMedian(
        List<Point2f[]> cornerSets,
        int expectedCornerCount
    )
    {
        Point2f[] fused = new Point2f[expectedCornerCount];
        for (int i = 0; i < expectedCornerCount; i++)
        {
            List<float> xs = new(cornerSets.Count);
            List<float> ys = new(cornerSets.Count);
            foreach (Point2f[] set in cornerSets)
            {
                xs.Add(set[i].X);
                ys.Add(set[i].Y);
            }

            xs.Sort();
            ys.Sort();
            int mid = xs.Count / 2;
            float mx = xs.Count % 2 == 1 ? xs[mid] : (xs[mid - 1] + xs[mid]) * 0.5f;
            float my = ys.Count % 2 == 1 ? ys[mid] : (ys[mid - 1] + ys[mid]) * 0.5f;
            fused[i] = new Point2f(mx, my);
        }

        return fused;
    }

    private static Point2f[] FuseCornersRobust(List<Point2f[]> cornerSets, int expectedCornerCount)
    {
        Point2f[] medianCorners = FuseCornersByMedian(cornerSets, expectedCornerCount);
        double[] rmsDistances = new double[cornerSets.Count];
        for (int setIndex = 0; setIndex < cornerSets.Count; setIndex++)
        {
            Point2f[] set = cornerSets[setIndex];
            double sumSquared = 0;
            for (int cornerIndex = 0; cornerIndex < expectedCornerCount; cornerIndex++)
            {
                double dx = set[cornerIndex].X - medianCorners[cornerIndex].X;
                double dy = set[cornerIndex].Y - medianCorners[cornerIndex].Y;
                sumSquared += (dx * dx) + (dy * dy);
            }

            rmsDistances[setIndex] = Math.Sqrt(sumSquared / expectedCornerCount);
        }

        double medianRms = ComputeMedian(rmsDistances);
        // 基于中位 RMS 的鲁棒门限，剔除明显离群帧。
        double inlierThreshold = Math.Max(0.5, medianRms * 2.5);

        List<int> inlierIndices = [];
        for (int i = 0; i < rmsDistances.Length; i++)
        {
            if (rmsDistances[i] <= inlierThreshold)
            {
                inlierIndices.Add(i);
            }
        }

        if (inlierIndices.Count == 0)
        {
            return medianCorners;
        }

        Point2f[] coarseFused = new Point2f[expectedCornerCount];
        for (int cornerIndex = 0; cornerIndex < expectedCornerCount; cornerIndex++)
        {
            double sumWeight = 0;
            double sumX = 0;
            double sumY = 0;
            foreach (int inlierIndex in inlierIndices)
            {
                // RMS 越小权重越大；0.25 防止单帧权重过高。
                double weight = 1.0 / Math.Max(0.25, rmsDistances[inlierIndex]);
                sumWeight += weight;
                sumX += cornerSets[inlierIndex][cornerIndex].X * weight;
                sumY += cornerSets[inlierIndex][cornerIndex].Y * weight;
            }

            coarseFused[cornerIndex] = new Point2f(
                (float)(sumX / sumWeight),
                (float)(sumY / sumWeight)
            );
        }

        // 第二阶段：角点级离群过滤 + 二次加权融合，处理个别帧中局部角点抖动/误检。
        Point2f[] refined = new Point2f[expectedCornerCount];
        for (int cornerIndex = 0; cornerIndex < expectedCornerCount; cornerIndex++)
        {
            double[] cornerDistances = new double[inlierIndices.Count];
            for (int i = 0; i < inlierIndices.Count; i++)
            {
                Point2f p = cornerSets[inlierIndices[i]][cornerIndex];
                double dx = p.X - coarseFused[cornerIndex].X;
                double dy = p.Y - coarseFused[cornerIndex].Y;
                cornerDistances[i] = Math.Sqrt((dx * dx) + (dy * dy));
            }

            double medianCornerDistance = ComputeMedian(cornerDistances);
            double cornerInlierThreshold = Math.Max(0.35, medianCornerDistance * 2.5);

            double sumWeight = 0;
            double sumX = 0;
            double sumY = 0;
            for (int i = 0; i < inlierIndices.Count; i++)
            {
                if (cornerDistances[i] > cornerInlierThreshold)
                {
                    continue;
                }

                int inlierIndex = inlierIndices[i];
                // 同时考虑帧整体稳定性(RMS)与该角点局部稳定性(distance)。
                double frameWeight = 1.0 / Math.Max(0.25, rmsDistances[inlierIndex]);
                double cornerWeight = 1.0 / Math.Max(0.15, cornerDistances[i]);
                double weight = frameWeight * cornerWeight;

                sumWeight += weight;
                sumX += cornerSets[inlierIndex][cornerIndex].X * weight;
                sumY += cornerSets[inlierIndex][cornerIndex].Y * weight;
            }

            if (sumWeight > 1e-9)
            {
                refined[cornerIndex] = new Point2f(
                    (float)(sumX / sumWeight),
                    (float)(sumY / sumWeight)
                );
            }
            else
            {
                refined[cornerIndex] = coarseFused[cornerIndex];
            }
        }

        return refined;
    }

    private static double ComputeMedian(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        double[] sorted = values.ToArray();
        Array.Sort(sorted);
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) * 0.5;
    }

    private async Task<Point2f[]?> TryFindPhysicalBoardCornersFromSampleAsync(
        ProjectorExtrinsicSampleGroup sample,
        Size patternSize,
        CalibProject project,
        int rotationAngle = 0
    )
    {
        IEnumerable<CalibPhotoRecord> candidates = new[] { sample.ProjectorOffPhoto }.Concat(
            sample.ProjectorPatternPhotos
        );

        foreach (CalibPhotoRecord candidate in candidates.DistinctBy(x => x.BlobKey))
        {
            byte[] bytes = await _blobContainer.GetAllBytesAsync(candidate.BlobKey);
            using Mat gray = LoadGrayMatWithRotation(bytes, rotationAngle);
            if (gray.Empty())
            {
                continue;
            }

            Point2f[]? corners = FindBoardPointsSubpixGray(
                gray,
                patternSize,
                GetDetectionBoardType(project, isProjectedBoard: false),
                project
            );
            if (corners != null)
            {
                int expectedCount = patternSize.Width * patternSize.Height;
                if (project.BoardType == CalibrationBoardType.MarkedSymmetricCircleGrid)
                {
                    expectedCount--;
                }
                if (corners.Length == expectedCount)
                {
                    return corners;
                }
            }
        }

        return null;
    }

    /// <summary>从多组外参样本中找重投影误差最小的一组（以白屏帧实体板位姿为准）</summary>
    private async Task<ProjectorExtrinsicSampleGroup?> FindBestExtrinsicAsync(
        List<ProjectorExtrinsicSampleGroup> photos,
        Mat cameraMatrix,
        Mat distCoeffs,
        Size patternSize,
        Point3f[] worldCorners,
        CalibProject project,
        int rotationAngle = 0
    )
    {
        ProjectorExtrinsicSampleGroup? best = null;
        double bestError = double.MaxValue;

        foreach (ProjectorExtrinsicSampleGroup sample in photos)
        {
            try
            {
                Point2f[]? corners = await TryFindPhysicalBoardCornersFromSampleAsync(
                    sample,
                    patternSize,
                    project,
                    rotationAngle
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

    private static Mat LoadGrayMatWithRotation(byte[] imageBytes, int rotationAngle)
    {
        using Mat mat = Cv2.ImDecode(imageBytes, ImreadModes.Grayscale);
        if (mat.Empty())
        {
            try
            {
                using SKBitmap? skBitmap = SKBitmap.Decode(imageBytes);
                if (skBitmap == null || skBitmap.IsNull)
                    return new Mat();

                using SKBitmap gray8 = skBitmap.Copy(SKColorType.Gray8);
                byte[] grayBytes = gray8.Bytes;
                mat.Dispose();
                using Mat grayMat = new(gray8.Height, gray8.Width, MatType.CV_8UC1);
                Marshal.Copy(grayBytes, 0, grayMat.Data, grayBytes.Length);
                return ApplyRotation(grayMat, rotationAngle);
            }
            catch
            {
                return new Mat();
            }
        }

        return ApplyRotation(mat, rotationAngle);
    }

    private static Mat ApplyRotation(Mat grayMat, int rotationAngle)
    {
        switch (rotationAngle)
        {
            case 90:
            {
                using Mat rot90 = new();
                Cv2.Transpose(grayMat, rot90);
                Cv2.Flip(rot90, rot90, FlipMode.Y);
                return rot90.Clone();
            }
            case 180:
            {
                using Mat rot180 = new();
                Cv2.Flip(grayMat, rot180, FlipMode.XY);
                return rot180.Clone();
            }
            case 270:
            {
                using Mat rot270 = new();
                Cv2.Transpose(grayMat, rot270);
                Cv2.Flip(rot270, rot270, FlipMode.X);
                return rot270.Clone();
            }
            default:
                return grayMat.Clone();
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
            BoardThicknessMm = project.BoardThicknessMm,
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
                ExtrinsicPhotoPhase.WhiteScreen => ExtrinsicPhotoPhaseDto.WhiteScreen,
                ExtrinsicPhotoPhase.Checkerboard => ExtrinsicPhotoPhaseDto.Checkerboard,
                _ => null,
            },
            ImageDiffScore = record.ImageDiffScore,
            ImageDiffSignificant = record.ImageDiffSignificant,
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

    /// <summary>
    /// 检测投影仪十字图（白底暗线）的交叉中心坐标。
    /// 策略：行/列投影均值 → 找亮度最低的列（垂直线）和行（水平线）→ 加权重心提升精度。
    /// </summary>
    private static Point2d? DetectCrossCenter(byte[] imageBytes, int rotationAngle = 0)
    {
        try
        {
            using Mat gray = LoadGrayMatWithRotation(imageBytes, rotationAngle);
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
