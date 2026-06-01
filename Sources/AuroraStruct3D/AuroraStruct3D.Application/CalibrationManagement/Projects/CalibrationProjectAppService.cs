using AuroraStruct3D.CalibrationManagement.Projects.Dtos;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Entities;

namespace AuroraStruct3D.CalibrationManagement.Projects;

/// <summary>
/// 标定工程应用服务实现。
/// 注：<see cref="CaptureFrameAsync"/> 与 <see cref="ComputeAsync"/> 当前为占位实现，
/// 抛 <see cref="NotImplementedException"/>，待 Phase 3 接入 OpenCV/SignalR/Hangfire 后落地。
/// </summary>
[Authorize(CalibrationPermissions.Project)]
public class CalibrationProjectAppService
    : AuroraStruct3DAppService,
        ICalibrationProjectAppService
{
    private readonly ICalibrationProjectRepository _projectRepository;

    public CalibrationProjectAppService(ICalibrationProjectRepository projectRepository)
    {
        _projectRepository = projectRepository;
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
    public Task<CalibrationCaptureFrameDto> CaptureFrameAsync(Guid id) =>
        // Phase 3 由 Hangfire Job 异步执行多相机同步触发 + BLOB 写入；本期仅占位。
        throw new NotImplementedException(
            "采集逻辑由 Phase 3 集成相机/电机驱动后实现，当前仅供 API 契约预览。"
        );

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
    public Task ComputeAsync(Guid id) =>
        // Phase 3 由 Hangfire Job 调用 OpenCV 完成相机内外参与结构光标定；本期仅占位。
        throw new NotImplementedException(
            "标定计算由 Phase 3 集成 OpenCvSharp 后实现，当前仅供 API 契约预览。"
        );

    // ═══════════════════════════════════════════════════════════════════════
    // 私有辅助
    // ═══════════════════════════════════════════════════════════════════════

    private async Task<CalibrationProject> LoadProjectWithDetailsAsync(Guid id)
    {
        CalibrationProject? project = await _projectRepository.FindWithDetailsAsync(id);
        return project ?? throw new EntityNotFoundException(typeof(CalibrationProject), id);
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
