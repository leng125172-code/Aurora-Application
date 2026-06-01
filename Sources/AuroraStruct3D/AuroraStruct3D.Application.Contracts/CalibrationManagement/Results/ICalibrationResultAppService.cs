using AuroraStruct3D.CalibrationManagement.Results.Dtos;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AuroraStruct3D.CalibrationManagement.Results;

/// <summary>
/// 标定结果应用服务接口。
/// 自动生成路径：/api/app/calibration-result
/// </summary>
public interface ICalibrationResultAppService : IApplicationService
{
    /// <summary>分页查询标定结果</summary>
    Task<PagedResultDto<CalibrationResultListDto>> GetListAsync(
        GetCalibrationResultListInput input
    );

    /// <summary>获取结果详情（含验证记录）</summary>
    Task<CalibrationResultDetailDto> GetAsync(Guid id);

    /// <summary>
    /// 切换为生效版本（同工程内其它版本自动取消生效）。
    /// </summary>
    Task<CalibrationResultDetailDto> SetActiveAsync(Guid id);

    /// <summary>新增一条验证记录</summary>
    Task<CalibrationValidationRecordDto> AddValidationAsync(
        Guid resultId,
        AddValidationRecordInput input
    );

    /// <summary>
    /// 导出标定结果为 JSON / ZIP（占位实现：抛 <see cref="NotImplementedException"/>，
    /// Phase 3 由 BLOB + IRemoteStreamContent 落地）。
    /// </summary>
    Task ExportAsync(Guid id);
}
