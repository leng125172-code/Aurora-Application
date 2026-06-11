using AuroraStruct3D.Calibration.Dtos;
using Volo.Abp.Application.Services;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// Step7 点云生成应用服务接口。
/// ABP 自动生成对应 REST 端点，路由基础路径：/api/app/calib-point-cloud
/// </summary>
public interface ICalibPointCloudAppService : IApplicationService
{
    /// <summary>
    /// 触发点云生成。
    /// POST /api/app/calib-point-cloud/generate
    /// </summary>
    Task<PointCloudStatusDto> GenerateAsync(GeneratePointCloudInput input);

    /// <summary>
    /// 取消正在进行的点云生成。
    /// POST /api/app/calib-point-cloud/cancel
    /// </summary>
    Task<PointCloudStatusDto> CancelAsync(Guid calibProjectId);

    /// <summary>
    /// 获取点云生成状态。
    /// GET /api/app/calib-point-cloud/status/{calibProjectId}
    /// </summary>
    Task<PointCloudStatusDto> GetStatusAsync(Guid calibProjectId);
}
