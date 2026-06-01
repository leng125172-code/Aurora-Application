using AuroraStruct3D.CalibrationManagement.Devices;
using AuroraStruct3D.CalibrationManagement.Projects.Dtos;
using AuroraStruct3D.CalibrationManagement.Projects.Jobs;
using AuroraStruct3D.Cameras;
using AuroraStruct3D.Cameras.Dtos;
using Hangfire;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.BlobStoring;
using Volo.Abp.Domain.Entities;

namespace AuroraStruct3D.CalibrationManagement.Projects;

/// <summary>
/// 标定工程应用服务实现。
/// </summary>
[Authorize(CalibrationPermissions.Project)]
public class CalibrationProjectAppService
    : AuroraStruct3DAppService,
        ICalibrationProjectAppService
{
    private readonly ICalibrationProjectRepository _projectRepository;
    private readonly ICalibrationDeviceRepository _deviceRepository;
    private readonly ICameraDeviceAppService _cameraAppService;
    private readonly IBlobContainer<CalibrationImageBlobContainer> _imageBlobContainer;
    private readonly IBackgroundJobClient _backgroundJobClient;

    public CalibrationProjectAppService(
        ICalibrationProjectRepository projectRepository,
        ICalibrationDeviceRepository deviceRepository,
        ICameraDeviceAppService cameraAppService,
        IBlobContainer<CalibrationImageBlobContainer> imageBlobContainer,
        IBackgroundJobClient backgroundJobClient
    )
    {
        _projectRepository = projectRepository;
        _deviceRepository = deviceRepository;
        _cameraAppService = cameraAppService;
        _imageBlobContainer = imageBlobContainer;
        _backgroundJobClient = backgroundJobClient;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 工程 CRUD
    // ═══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public async Task<PagedResultDto<CalibrationProjectListDto>> GetListAsync(
        GetCalibrationProjectListInput input
    )
    {
        int totalCount = await _projectRepository.GetCountAsync(
            input.Filter,
            input.CalibrationDeviceId,
            input.Status
        );

        List<CalibrationProject> projects = await _projectRepository.GetListAsync(
            input.Filter,
            input.CalibrationDeviceId,
            input.Status,
            input.SkipCount,
            input.MaxResultCount,
            input.Sorting
        );

        return new PagedResultDto<CalibrationProjectListDto>(
            totalCount,
            projects.Select(MapToListDto).ToList()
        );
    }

    /// <inheritdoc/>
    public async Task<CalibrationProjectDetailDto> GetAsync(Guid id) =>
        MapToDetailDto(await LoadProjectWithDetailsAsync(id));

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.ProjectCreate)]
    public async Task<CalibrationProjectDetailDto> CreateAsync(CreateCalibrationProjectDto input)
    {
        CalibrationProject project = new(
            GuidGenerator.Create(),
            input.Name,
            input.CalibrationDeviceId,
            input.Description
        );
        await _projectRepository.InsertAsync(project, autoSave: true);
        return MapToDetailDto(project);
    }

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.ProjectUpdate)]
    public async Task<CalibrationProjectDetailDto> UpdateAsync(
        Guid id,
        UpdateCalibrationProjectDto input
    )
    {
        CalibrationProject project = await LoadProjectWithDetailsAsync(id);
        project.UpdateBasicInfo(input.Name, input.Description);
        await _projectRepository.UpdateAsync(project, autoSave: true);
        return MapToDetailDto(project);
    }

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.ProjectDelete)]
    public async Task DeleteAsync(Guid id) =>
        await _projectRepository.DeleteAsync(id, autoSave: true);

    // ═══════════════════════════════════════════════════════════════════════
    // 配置
    // ═══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.ProjectUpdate)]
    public async Task<CalibrationProjectDetailDto> SetBoardConfigAsync(
        Guid id,
        SetBoardConfigInput input
    )
    {
        CalibrationProject project = await LoadProjectWithDetailsAsync(id);
        project.SetBoardConfig(
            input.BoardType,
            input.Rows,
            input.Cols,
            input.ManufactureAccuracyMm,
            input.SquareSizeMm,
            input.CircleDiameterMm,
            input.CircleSpacingMm,
            input.AprilTagFamily,
            input.AprilTagSizeMm,
            input.AprilTagSpacingMm
        );
        await _projectRepository.UpdateAsync(project, autoSave: true);
        return MapToDetailDto(project);
    }

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.ProjectUpdate)]
    public async Task<CalibrationProjectDetailDto> SetCaptureConfigAsync(
        Guid id,
        SetCaptureConfigInput input
    )
    {
        CalibrationProject project = await LoadProjectWithDetailsAsync(id);
        project.SetCaptureConfig(
            input.TargetCaptureCount,
            input.UnifiedExposureUs,
            input.UnifiedGainDb,
            input.ImageFormat,
            input.UnifiedWhiteBalance,
            input.StructuredLightBrightness,
            input.PatternIntervalMs,
            input.CapturesPerPhase
        );
        await _projectRepository.UpdateAsync(project, autoSave: true);
        return MapToDetailDto(project);
    }

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.ProjectUpdate)]
    public async Task<CalibrationProjectDetailDto> TransitionStatusAsync(
        Guid id,
        TransitionProjectStatusInput input
    )
    {
        CalibrationProject project = await LoadProjectWithDetailsAsync(id);
        project.TransitionStatus(input.Status, input.FailureReason);
        await _projectRepository.UpdateAsync(project, autoSave: true);
        return MapToDetailDto(project);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 采集帧管理
    // ═══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.ProjectCapture)]
    public async Task<CalibrationCaptureFrameDto> CaptureFrameAsync(Guid id)
    {
        CalibrationProject project = await LoadProjectWithDetailsAsync(id);

        // 加载标定设备（含相机绑定）以获取参与采集的相机列表
        CalibrationDevice? device = await _deviceRepository.FindWithDetailsAsync(
            project.CalibrationDeviceId
        );
        if (device is null)
        {
            throw new EntityNotFoundException(
                typeof(CalibrationDevice),
                project.CalibrationDeviceId
            );
        }
        if (device.CameraBindings.Count == 0)
        {
            throw new BusinessException("Calibration:NoCameraBound").WithData(
                "deviceId",
                device.Id
            );
        }

        // 计算下一帧序号（工程内单调递增，不复用已删除的序号）
        int nextFrameIndex =
            (project.Frames.Count > 0 ? project.Frames.Max(f => f.FrameIndex) : 0) + 1;

        CalibrationCaptureFrame frame = new(GuidGenerator.Create(), project.Id, nextFrameIndex);

        // 依次触发每台相机的同步快照（顺序执行避免 USB 总线带宽冲突）
        foreach (CalibrationCameraBinding binding in device.CameraBindings)
        {
            CameraSnapshotDto snapshot = await _cameraAppService.TakeSnapshotAsync(
                binding.CameraDeviceId
            );

            (byte[] jpegBytes, int width, int height) = DecodeJpegDataUri(snapshot.DataUri);

            string blobName =
                $"{project.Id}/frame-{nextFrameIndex:D4}/camera-{binding.CameraDeviceId}.jpg";
            await _imageBlobContainer.SaveAsync(blobName, jpegBytes, overrideExisting: true);

            CalibrationCaptureImage image = new(
                GuidGenerator.Create(),
                frame.Id,
                binding.CameraDeviceId,
                binding.Role,
                blobName,
                width,
                height,
                jpegBytes.LongLength
            );
            frame.Images.Add(image);
        }

        project.Frames.Add(frame);

        // 首帧采集时自动将工程状态从"配置中/草稿"切换为"采集中"
        if (
            project.Status == CalibrationProjectStatus.Draft
            || project.Status == CalibrationProjectStatus.Configuring
        )
        {
            project.TransitionStatus(CalibrationProjectStatus.Capturing);
        }

        await _projectRepository.UpdateAsync(project, autoSave: true);
        return MapToFrameDto(frame);
    }

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.ProjectUpdate)]
    public async Task RejectFrameAsync(Guid projectId, Guid frameId, RejectFrameInput input)
    {
        CalibrationProject project = await LoadProjectWithDetailsAsync(projectId);
        CalibrationCaptureFrame frame =
            project.Frames.FirstOrDefault(x => x.Id == frameId)
            ?? throw new EntityNotFoundException(typeof(CalibrationCaptureFrame), frameId);
        frame.Reject(input.Reason);
        await _projectRepository.UpdateAsync(project, autoSave: true);
    }

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.ProjectUpdate)]
    public async Task AcceptFrameAsync(Guid projectId, Guid frameId)
    {
        CalibrationProject project = await LoadProjectWithDetailsAsync(projectId);
        CalibrationCaptureFrame frame =
            project.Frames.FirstOrDefault(x => x.Id == frameId)
            ?? throw new EntityNotFoundException(typeof(CalibrationCaptureFrame), frameId);
        frame.Accept();
        await _projectRepository.UpdateAsync(project, autoSave: true);
    }

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.ProjectUpdate)]
    public async Task DeleteFrameAsync(Guid projectId, Guid frameId)
    {
        CalibrationProject project = await LoadProjectWithDetailsAsync(projectId);
        CalibrationCaptureFrame frame =
            project.Frames.FirstOrDefault(x => x.Id == frameId)
            ?? throw new EntityNotFoundException(typeof(CalibrationCaptureFrame), frameId);
        project.Frames.Remove(frame);
        await _projectRepository.UpdateAsync(project, autoSave: true);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 计算
    // ═══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.ProjectCompute)]
    public async Task ComputeAsync(Guid id)
    {
        // 校验工程存在且至少有一帧已被接受
        CalibrationProject project = await LoadProjectWithDetailsAsync(id);
        if (!project.Frames.Any(f => f.IsAccepted))
        {
            throw new BusinessException("Calibration:NoAcceptedFrame").WithData("projectId", id);
        }

        // 异步入队 Hangfire Job，接口立即返回
        _backgroundJobClient.Enqueue<CalibrationComputeJob>(job =>
            job.ExecuteAsync(new CalibrationComputeJobArgs { CalibrationProjectId = id })
        );
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 私有辅助
    // ═══════════════════════════════════════════════════════════════════════

    private async Task<CalibrationProject> LoadProjectWithDetailsAsync(Guid id)
    {
        CalibrationProject? project = await _projectRepository.FindWithDetailsAsync(id);
        return project ?? throw new EntityNotFoundException(typeof(CalibrationProject), id);
    }

    /// <summary>
    /// 解析 "data:image/jpeg;base64,xxx" 格式的数据 URI，返回原始字节及 JPEG 宽高。
    /// 通过扫描 JPEG SOF（Start Of Frame）标记 0xFFC0~0xFFCF 读取分辨率，
    /// 避免引入额外图像库依赖。
    /// </summary>
    private static (byte[] Bytes, int Width, int Height) DecodeJpegDataUri(string dataUri)
    {
        if (string.IsNullOrWhiteSpace(dataUri))
        {
            throw new BusinessException("Calibration:EmptyCameraSnapshot");
        }

        // 兼容带前缀与不带前缀两种形式
        string base64 = dataUri;
        int commaIndex = dataUri.IndexOf(',');
        if (commaIndex > 0 && dataUri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            base64 = dataUri[(commaIndex + 1)..];
        }

        byte[] bytes = Convert.FromBase64String(base64);
        (int width, int height) = ReadJpegDimensions(bytes);
        return (bytes, width, height);
    }

    /// <summary>
    /// 从 JPEG 字节流读取宽高。失败时返回 (0,0)，由调用方决定是否容忍。
    /// </summary>
    private static (int Width, int Height) ReadJpegDimensions(byte[] bytes)
    {
        // SOI 必须是 0xFFD8
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
        {
            return (0, 0);
        }

        int i = 2;
        while (i < bytes.Length - 8)
        {
            // 段头：0xFF + marker
            if (bytes[i] != 0xFF)
            {
                i++;
                continue;
            }

            byte marker = bytes[i + 1];
            // SOF0~SOF15（除 DHT=0xC4、JPG=0xC8、DAC=0xCC）
            if (
                marker >= 0xC0
                && marker <= 0xCF
                && marker != 0xC4
                && marker != 0xC8
                && marker != 0xCC
            )
            {
                int height = (bytes[i + 5] << 8) | bytes[i + 6];
                int width = (bytes[i + 7] << 8) | bytes[i + 8];
                return (width, height);
            }

            // 跳到下一段：段长度位于 marker 后两字节（大端）
            int segLen = (bytes[i + 2] << 8) | bytes[i + 3];
            i += 2 + segLen;
        }

        return (0, 0);
    }

    private static CalibrationProjectListDto MapToListDto(CalibrationProject entity) =>
        new()
        {
            Id = entity.Id,
            CreationTime = entity.CreationTime,
            CreatorId = entity.CreatorId,
            LastModificationTime = entity.LastModificationTime,
            LastModifierId = entity.LastModifierId,
            Name = entity.Name,
            Description = entity.Description,
            CalibrationDeviceId = entity.CalibrationDeviceId,
            Status = entity.Status,
            StartedTime = entity.StartedTime,
            CompletedTime = entity.CompletedTime,
            FrameCount = entity.Frames.Count,
            AcceptedFrameCount = entity.Frames.Count(f => f.IsAccepted),
            TargetCaptureCount = entity.TargetCaptureCount,
        };

    private static CalibrationProjectDetailDto MapToDetailDto(CalibrationProject entity) =>
        new()
        {
            Id = entity.Id,
            CreationTime = entity.CreationTime,
            CreatorId = entity.CreatorId,
            LastModificationTime = entity.LastModificationTime,
            LastModifierId = entity.LastModifierId,
            Name = entity.Name,
            Description = entity.Description,
            CalibrationDeviceId = entity.CalibrationDeviceId,
            Status = entity.Status,
            StartedTime = entity.StartedTime,
            CompletedTime = entity.CompletedTime,
            FailureReason = entity.FailureReason,
            FrameCount = entity.Frames.Count,
            AcceptedFrameCount = entity.Frames.Count(f => f.IsAccepted),
            TargetCaptureCount = entity.TargetCaptureCount,
            BoardType = entity.BoardType,
            BoardRows = entity.BoardRows,
            BoardCols = entity.BoardCols,
            SquareSizeMm = entity.SquareSizeMm,
            CircleDiameterMm = entity.CircleDiameterMm,
            CircleSpacingMm = entity.CircleSpacingMm,
            AprilTagFamily = entity.AprilTagFamily,
            AprilTagSizeMm = entity.AprilTagSizeMm,
            AprilTagSpacingMm = entity.AprilTagSpacingMm,
            BoardManufactureAccuracyMm = entity.BoardManufactureAccuracyMm,
            UnifiedExposureUs = entity.UnifiedExposureUs,
            UnifiedGainDb = entity.UnifiedGainDb,
            UnifiedWhiteBalance = entity.UnifiedWhiteBalance,
            ImageFormat = entity.ImageFormat,
            StructuredLightBrightness = entity.StructuredLightBrightness,
            PatternIntervalMs = entity.PatternIntervalMs,
            CapturesPerPhase = entity.CapturesPerPhase,
            Frames = entity.Frames.OrderBy(f => f.FrameIndex).Select(MapToFrameDto).ToList(),
        };

    private static CalibrationCaptureFrameDto MapToFrameDto(CalibrationCaptureFrame entity) =>
        new()
        {
            Id = entity.Id,
            CalibrationProjectId = entity.CalibrationProjectId,
            FrameIndex = entity.FrameIndex,
            CapturedTime = entity.CapturedTime,
            IsAccepted = entity.IsAccepted,
            RejectionReason = entity.RejectionReason,
            Images = entity.Images.Select(MapToImageDto).ToList(),
        };

    private static CalibrationCaptureImageDto MapToImageDto(CalibrationCaptureImage entity) =>
        new()
        {
            Id = entity.Id,
            CalibrationCaptureFrameId = entity.CalibrationCaptureFrameId,
            CameraDeviceId = entity.CameraDeviceId,
            CameraRole = entity.CameraRole,
            BlobName = entity.BlobName,
            Width = entity.Width,
            Height = entity.Height,
            FileSizeBytes = entity.FileSizeBytes,
            ReprojectionError = entity.ReprojectionError,
        };
}
