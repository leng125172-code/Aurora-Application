using AuroraStruct3D.CalibrationManagement.Results.Dtos;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Entities;

namespace AuroraStruct3D.CalibrationManagement.Results;

/// <summary>
/// 标定结果应用服务实现。
/// </summary>
[Authorize(CalibrationPermissions.Result)]
public class CalibrationResultAppService
    : AuroraStruct3DAppService,
        ICalibrationResultAppService
{
    private readonly ICalibrationResultRepository _resultRepository;

    public CalibrationResultAppService(ICalibrationResultRepository resultRepository)
    {
        _resultRepository = resultRepository;
    }

    /// <inheritdoc/>
    public async Task<PagedResultDto<CalibrationResultListDto>> GetListAsync(
        GetCalibrationResultListInput input
    )
    {
        int totalCount = await _resultRepository.GetCountAsync(
            input.CalibrationProjectId,
            input.CalibrationDeviceId,
            input.IsActive
        );

        List<CalibrationResult> results = await _resultRepository.GetListAsync(
            input.CalibrationProjectId,
            input.CalibrationDeviceId,
            input.IsActive,
            input.SkipCount,
            input.MaxResultCount,
            input.Sorting
        );

        return new PagedResultDto<CalibrationResultListDto>(
            totalCount,
            results.Select(MapToListDto).ToList()
        );
    }

    /// <inheritdoc/>
    public async Task<CalibrationResultDetailDto> GetAsync(Guid id) =>
        MapToDetailDto(await LoadResultWithDetailsAsync(id));

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.ResultSetActive)]
    public async Task<CalibrationResultDetailDto> SetActiveAsync(Guid id)
    {
        CalibrationResult target = await LoadResultWithDetailsAsync(id);

        // 取消同工程其它已生效版本（保证唯一生效）
        List<CalibrationResult> otherActives = await _resultRepository.GetActiveResultsAsync(
            target.CalibrationProjectId,
            excludeResultId: id
        );
        foreach (CalibrationResult other in otherActives)
        {
            other.SetActive(false);
            await _resultRepository.UpdateAsync(other);
        }

        target.SetActive(true);
        await _resultRepository.UpdateAsync(target, autoSave: true);
        return MapToDetailDto(target);
    }

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.ProjectValidate)]
    public async Task<CalibrationValidationRecordDto> AddValidationAsync(
        Guid resultId,
        AddValidationRecordInput input
    )
    {
        CalibrationResult result = await LoadResultWithDetailsAsync(resultId);
        CalibrationValidationRecord record = new(
            GuidGenerator.Create(),
            resultId,
            input.ValidationType,
            input.IsPassed,
            input.MetricsJson,
            input.ReportBlobName,
            input.Remarks
        );
        result.Validations.Add(record);
        await _resultRepository.UpdateAsync(result, autoSave: true);
        return MapToValidationDto(record);
    }

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.ResultExport)]
    public Task ExportAsync(Guid id) =>
        // Phase 3 由 BLOB + IRemoteStreamContent 落地；本期仅占位。
        throw new NotImplementedException(
            "导出标定结果由 Phase 3 通过 BLOB 容器实现，当前仅供 API 契约预览。"
        );

    // ─────────────────────────── 私有辅助 ───────────────────────────

    private async Task<CalibrationResult> LoadResultWithDetailsAsync(Guid id)
    {
        CalibrationResult? result = await _resultRepository.FindWithDetailsAsync(id);
        return result ?? throw new EntityNotFoundException(typeof(CalibrationResult), id);
    }

    private static CalibrationResultListDto MapToListDto(CalibrationResult entity) =>
        new()
        {
            Id = entity.Id,
            CreationTime = entity.CreationTime,
            CreatorId = entity.CreatorId,
            LastModificationTime = entity.LastModificationTime,
            LastModifierId = entity.LastModifierId,
            CalibrationProjectId = entity.CalibrationProjectId,
            CalibrationDeviceId = entity.CalibrationDeviceId,
            Version = entity.Version,
            IsActive = entity.IsActive,
            ComputedTime = entity.ComputedTime,
            OverallReprojectionError = entity.OverallReprojectionError,
            RmsError = entity.RmsError,
            ValidationCount = entity.Validations.Count,
        };

    private static CalibrationResultDetailDto MapToDetailDto(CalibrationResult entity) =>
        new()
        {
            Id = entity.Id,
            CreationTime = entity.CreationTime,
            CreatorId = entity.CreatorId,
            LastModificationTime = entity.LastModificationTime,
            LastModifierId = entity.LastModifierId,
            CalibrationProjectId = entity.CalibrationProjectId,
            CalibrationDeviceId = entity.CalibrationDeviceId,
            Version = entity.Version,
            IsActive = entity.IsActive,
            ComputedTime = entity.ComputedTime,
            OverallReprojectionError = entity.OverallReprojectionError,
            RmsError = entity.RmsError,
            ValidationCount = entity.Validations.Count,
            CameraIntrinsicsJson = entity.CameraIntrinsicsJson,
            CameraExtrinsicsJson = entity.CameraExtrinsicsJson,
            StructuredLightCalibrationJson = entity.StructuredLightCalibrationJson,
            MaxError = entity.MaxError,
            MinError = entity.MinError,
            MeanError = entity.MeanError,
            Validations = entity
                .Validations.OrderByDescending(v => v.ValidatedTime)
                .Select(MapToValidationDto)
                .ToList(),
        };

    private static CalibrationValidationRecordDto MapToValidationDto(
        CalibrationValidationRecord entity
    ) =>
        new()
        {
            Id = entity.Id,
            CalibrationResultId = entity.CalibrationResultId,
            ValidationType = entity.ValidationType,
            IsPassed = entity.IsPassed,
            MetricsJson = entity.MetricsJson,
            ReportBlobName = entity.ReportBlobName,
            Remarks = entity.Remarks,
            ValidatedTime = entity.ValidatedTime,
        };
}
