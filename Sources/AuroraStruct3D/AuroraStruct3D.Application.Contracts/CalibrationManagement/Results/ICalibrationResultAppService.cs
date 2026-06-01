using AuroraStruct3D.CalibrationManagement.Results.Dtos;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

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
    /// 按指定格式导出标定结果（流式下载）。
    /// 包含相机内外参、结构光标定、误差统计、验证记录等完整快照。
    /// 支持 JSON / XML / YAML / TXT 四种格式，默认 JSON。
    /// </summary>
    /// <param name="id">标定结果 Id</param>
    /// <param name="format">导出格式（缺省 JSON）</param>
    Task<IRemoteStreamContent> ExportAsync(
        Guid id,
        CalibrationResultExportFormat format = CalibrationResultExportFormat.Json
    );
}
