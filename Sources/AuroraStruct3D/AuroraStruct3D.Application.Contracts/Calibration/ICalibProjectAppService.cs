using AuroraStruct3D.Calibration.Dtos;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// 设备标定项目应用服务接口。
/// ABP 自动生成对应 REST 端点，路由基础路径：/api/app/calib-project
/// </summary>
public interface ICalibProjectAppService : IApplicationService
{
    /// <summary>
    /// 分页查询标定项目列表（支持名称/设备类型/状态/时间等多条件过滤）。
    /// GET /api/app/calib-project
    /// </summary>
    Task<PagedResultDto<CalibProjectDto>> GetListAsync(GetCalibProjectListInput input);

    /// <summary>
    /// 获取单个标定项目详情。
    /// GET /api/app/calib-project/{id}
    /// </summary>
    Task<CalibProjectDto> GetAsync(Guid id);

    /// <summary>
    /// 新增标定项目（要求设备处于手动或检修模式）。
    /// POST /api/app/calib-project
    /// </summary>
    Task<CalibProjectDto> CreateAsync(CreateCalibProjectInput input);

    /// <summary>
    /// 修改标定项目名称与描述（要求设备处于手动或检修模式）。
    /// PUT /api/app/calib-project/{id}
    /// </summary>
    Task<CalibProjectDto> UpdateAsync(Guid id, UpdateCalibProjectInput input);

    /// <summary>
    /// 删除标定项目（要求设备处于手动或检修模式）。
    /// DELETE /api/app/calib-project/{id}
    /// </summary>
    Task DeleteAsync(Guid id);
}
