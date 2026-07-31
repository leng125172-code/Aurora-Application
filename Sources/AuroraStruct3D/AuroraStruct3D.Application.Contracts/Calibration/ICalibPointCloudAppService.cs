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

    /// <summary>
    /// 下载点云 PLY 文件。
    /// GET /api/app/calib-point-cloud/download/{calibProjectId}
    /// </summary>
    Task<byte[]> DownloadPlyAsync(Guid calibProjectId);

    /// <summary>
    /// 增量点云生成（在线扫描时每轮调用）。
    /// POST /api/app/calib-point-cloud/generate-incremental
    /// </summary>
    Task GenerateIncrementalPointCloudAsync(
        Guid calibProjectId,
        long roundIndex,
        int patternCount);

    /// <summary>
    /// 使用同一轮的一组普通双目图像生成增量点云（不依赖投影条纹）。
    /// </summary>
    Task GenerateIncrementalStereoPointCloudAsync(Guid calibProjectId, long roundIndex);

    /// <summary>
    /// 完成增量点云模式，合并所有累积的点云数据。
    /// POST /api/app/calib-point-cloud/complete-incremental
    /// </summary>
    Task CompleteIncrementalPointCloudAsync(Guid calibProjectId);
}
