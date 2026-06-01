using System.Text;
using System.Text.Json;
using AuroraStruct3D.CalibrationManagement.Results.Dtos;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Content;
using Volo.Abp.Domain.Entities;

namespace AuroraStruct3D.CalibrationManagement.Results;

/// <summary>
/// 标定结果应用服务实现。
/// </summary>
[Authorize(CalibrationPermissions.Result)]
public class CalibrationResultAppService
    : CalibrationAppServiceBase,
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
        EnsureManualOrMaintenanceMode();
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
        EnsureManualOrMaintenanceMode();
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
    public async Task<IRemoteStreamContent> ExportAsync(Guid id)
    {
        CalibrationResult result = await LoadResultWithDetailsAsync(id);

        // 组装统一导出对象：包含元数据、所有参数 JSON 与误差统计、验证记录摘要
        var exportObject = new
        {
            schemaVersion = "1.0",
            exportedAt = DateTime.UtcNow,
            result = new
            {
                id = result.Id,
                calibrationProjectId = result.CalibrationProjectId,
                calibrationDeviceId = result.CalibrationDeviceId,
                version = result.Version,
                isActive = result.IsActive,
                computedTime = result.ComputedTime,
                errorStatistics = new
                {
                    overall = result.OverallReprojectionError,
                    max = result.MaxError,
                    min = result.MinError,
                    mean = result.MeanError,
                    rms = result.RmsError,
                },
                cameraIntrinsics = ParseJsonOrRaw(result.CameraIntrinsicsJson),
                cameraExtrinsics = ParseJsonOrRaw(result.CameraExtrinsicsJson),
                structuredLightCalibration = ParseJsonOrRaw(result.StructuredLightCalibrationJson),
            },
            validations = result
                .Validations.OrderByDescending(v => v.ValidatedTime)
                .Select(v => new
                {
                    id = v.Id,
                    type = v.ValidationType.ToString(),
                    isPassed = v.IsPassed,
                    validatedTime = v.ValidatedTime,
                    metrics = ParseJsonOrRaw(v.MetricsJson),
                    reportBlobName = v.ReportBlobName,
                    remarks = v.Remarks,
                })
                .ToList(),
        };

        JsonSerializerOptions jsonOpts = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(exportObject, jsonOpts));

        string fileName =
            $"calibration-result-{result.CalibrationProjectId:N}-v{result.Version}.json";
        return new RemoteStreamContent(
            new MemoryStream(payload),
            fileName,
            contentType: "application/json"
        );
    }

    // ─────────────────────────── 私有辅助 ───────────────────────────

    private async Task<CalibrationResult> LoadResultWithDetailsAsync(Guid id)
    {
        CalibrationResult? result = await _resultRepository.FindWithDetailsAsync(id);
        return result ?? throw new EntityNotFoundException(typeof(CalibrationResult), id);
    }

    /// <summary>
    /// 将存储的 JSON 字符串解析为 JsonElement 以便嵌入到导出结构中；
    /// 解析失败或为空则按原始字符串/ null 处理。
    /// </summary>
    private static object? ParseJsonOrRaw(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return json;
        }
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
