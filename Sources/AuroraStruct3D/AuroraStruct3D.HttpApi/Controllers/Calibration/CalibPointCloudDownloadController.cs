using AuroraStruct3D.Calibration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuroraStruct3D.Controllers.Calibration;

/// <summary>
/// 点云文件下载接口。
/// 使用显式路由返回原始 PLY 字节，避免 ABP 动态 API 按 DownloadPly 方法名生成
/// /download-ply 路由并将 byte[] 包装为 JSON，导致状态中 /download 地址不可用。
/// </summary>
[Authorize]
[Route("api/app/calib-point-cloud")]
public class CalibPointCloudDownloadController : AuroraStruct3DController
{
    private readonly ICalibPointCloudAppService _pointCloudAppService;

    public CalibPointCloudDownloadController(ICalibPointCloudAppService pointCloudAppService)
    {
        _pointCloudAppService = pointCloudAppService;
    }

    /// <summary>下载指定标定项目已生成完成的 PLY 点云。</summary>
    [HttpGet("download/{calibProjectId:guid}")]
    [Produces("application/ply")]
    public async Task<IActionResult> DownloadAsync(Guid calibProjectId)
    {
        byte[] plyBytes = await _pointCloudAppService.DownloadPlyAsync(calibProjectId);
        return File(plyBytes, "application/ply", $"pointcloud-{calibProjectId:N}.ply");
    }
}
