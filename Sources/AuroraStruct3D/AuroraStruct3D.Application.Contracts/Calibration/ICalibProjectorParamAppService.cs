using AuroraStruct3D.Calibration.Dtos;
using Volo.Abp.Application.Services;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// 标定投影仪参数应用服务接口（Step3 投影仪参数配置页）。
/// ABP 自动生成对应 REST 端点，路由基础路径：/api/app/calib-projector-param
/// </summary>
public interface ICalibProjectorParamAppService : IApplicationService
{
    /// <summary>
    /// 获取指定标定项目的投影仪参数配置。
    /// 若数据库中尚无记录则返回 null，由前端使用默认值。
    /// GET /api/app/calib-projector-param/{calibProjectId}
    /// </summary>
    Task<CalibProjectorParamDto?> GetAsync(Guid calibProjectId);

    /// <summary>
    /// 保存（Upsert）投影仪参数配置。
    /// 若该标定项目下已存在记录则更新，否则新建。
    /// PUT /api/app/calib-projector-param/{calibProjectId}
    /// </summary>
    Task<CalibProjectorParamDto> UpdateAsync(
        Guid calibProjectId,
        SaveCalibProjectorParamInput input
    );
}
