using AuroraStruct3D.Calibration.Dtos;
using Volo.Abp.Application.Services;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// Step6 在线扫描应用服务接口。
/// ABP 自动生成对应 REST 端点，路由基础路径：/api/app/calib-scan
/// </summary>
public interface ICalibScanAppService : IApplicationService
{
    /// <summary>
    /// 启动在线扫描会话。
    /// POST /api/app/calib-scan/start
    /// </summary>
    Task<CalibScanStatusDto> StartAsync(StartCalibScanInput input);

    /// <summary>
    /// 停止在线扫描会话。
    /// POST /api/app/calib-scan/stop
    /// </summary>
    Task<CalibScanStatusDto> StopAsync(StopCalibScanInput input);

    /// <summary>
    /// 获取在线扫描状态。
    /// GET /api/app/calib-scan/status/{calibProjectId}
    /// </summary>
    Task<CalibScanStatusDto> GetStatusAsync(Guid calibProjectId);

    /// <summary>
    /// 设置当前扫描会话的 OpenCV 图像自动优化开关。
    /// 启用后，后续抓帧将通过 CLAHE 算法自动增强对比度。
    /// POST /api/app/calib-scan/set-image-enhance
    /// </summary>
    Task SetImageEnhanceAsync(SetCalibScanImageEnhanceInput input);
}
