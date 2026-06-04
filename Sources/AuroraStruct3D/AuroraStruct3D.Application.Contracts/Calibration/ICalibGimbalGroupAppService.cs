using AuroraStruct3D.Calibration.Dtos;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// 云台组应用服务接口。
/// ABP 自动生成对应 REST 端点，路由基础路径：/api/app/calib-gimbal-group
/// </summary>
public interface ICalibGimbalGroupAppService : IApplicationService
{
    /// <summary>
    /// 分页查询云台组列表。
    /// GET /api/app/calib-gimbal-group
    /// </summary>
    Task<PagedResultDto<CalibGimbalGroupDto>> GetListAsync(GetCalibGimbalGroupListInput input);

    /// <summary>
    /// 获取单个云台组详情。
    /// GET /api/app/calib-gimbal-group/{id}
    /// </summary>
    Task<CalibGimbalGroupDto> GetAsync(Guid id);

    /// <summary>
    /// 新增云台组（要求设备处于手动或检修模式）。
    /// POST /api/app/calib-gimbal-group
    /// </summary>
    Task<CalibGimbalGroupDto> CreateAsync(CreateUpdateCalibGimbalGroupInput input);

    /// <summary>
    /// 修改云台组（要求设备处于手动或检修模式）。
    /// PUT /api/app/calib-gimbal-group/{id}
    /// </summary>
    Task<CalibGimbalGroupDto> UpdateAsync(Guid id, CreateUpdateCalibGimbalGroupInput input);

    /// <summary>
    /// 删除云台组（要求设备处于手动或检修模式）。
    /// DELETE /api/app/calib-gimbal-group/{id}
    /// </summary>
    Task DeleteAsync(Guid id);
}
