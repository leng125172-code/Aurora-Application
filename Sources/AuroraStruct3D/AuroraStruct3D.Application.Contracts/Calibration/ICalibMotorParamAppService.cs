using AuroraStruct3D.Calibration.Dtos;
using Volo.Abp.Application.Services;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// 标定电机参数应用服务接口。
/// 路由基础路径：/api/app/calib-motor-param
/// </summary>
public interface ICalibMotorParamAppService : IApplicationService
{
    /// <summary>
    /// 获取指定标定项目的所有电机参数记录。
    /// GET /api/app/calib-motor-param?calibProjectId={id}
    /// </summary>
    Task<List<CalibMotorParamDto>> GetListAsync(Guid calibProjectId);

    /// <summary>
    /// 保存（Upsert）电机参数。
    /// POST /api/app/calib-motor-param/save
    /// </summary>
    Task<CalibMotorParamDto> SaveAsync(SaveCalibMotorParamInput input);

    /// <summary>
    /// 删除指定电机参数记录。
    /// DELETE /api/app/calib-motor-param/{id}
    /// </summary>
    Task DeleteAsync(Guid id);
}
