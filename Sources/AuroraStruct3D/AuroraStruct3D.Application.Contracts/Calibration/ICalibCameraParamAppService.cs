using AuroraStruct3D.Calibration.Dtos;
using Volo.Abp.Application.Services;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// 标定相机参数应用服务接口。
/// ABP 自动生成对应 REST 端点，路由基础路径：/api/app/calib-camera-param
/// </summary>
public interface ICalibCameraParamAppService : IApplicationService
{
    /// <summary>
    /// 获取指定标定项目的所有相机参数记录。
    /// GET /api/app/calib-camera-param?calibProjectId={id}
    /// </summary>
    Task<List<CalibCameraParamDto>> GetListAsync(Guid calibProjectId);

    /// <summary>
    /// 保存（Upsert）相机参数。
    /// 若该标定项目下已存在同一相机设备的记录则更新，否则新建。
    /// POST /api/app/calib-camera-param
    /// </summary>
    Task<CalibCameraParamDto> SaveAsync(SaveCalibCameraParamInput input);

    /// <summary>
    /// 删除指定的相机参数记录。
    /// DELETE /api/app/calib-camera-param/{id}
    /// </summary>
    Task DeleteAsync(Guid id);

    /// <summary>
    /// 校验 Step 2 相机参数是否已全部配置完成。
    /// 项目绑定的所有相机在 AbpProCalibCameraParams 中均有记录且传感器基础参数已填写。
    /// GET /api/app/calib-camera-param/validate-step2?calibProjectId={id}
    /// </summary>
    Task<bool> ValidateStep2Async(Guid calibProjectId);
}
