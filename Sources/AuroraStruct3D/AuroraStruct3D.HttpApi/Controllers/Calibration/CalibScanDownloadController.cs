using AuroraStruct3D.Calibration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuroraStruct3D.Controllers.Calibration;

/// <summary>在线扫描原始图片诊断包下载接口。</summary>
[Authorize]
[Route("api/app/calib-scan")]
public class CalibScanDownloadController : AuroraStruct3DController
{
    private readonly ICalibScanAppService _scanAppService;

    public CalibScanDownloadController(ICalibScanAppService scanAppService)
    {
        _scanAppService = scanAppService;
    }

    /// <summary>下载指定项目、指定轮次的全部主从相机扫描图片。</summary>
    [HttpGet("download-round/{calibProjectId:guid}/{roundIndex:long}")]
    [Produces("application/zip")]
    public async Task<IActionResult> DownloadRoundAsync(Guid calibProjectId, long roundIndex)
    {
        byte[] zipBytes = await _scanAppService.DownloadRoundImagesAsync(calibProjectId, roundIndex);
        return File(zipBytes, "application/zip",
            $"calib-scan-{calibProjectId:N}-round-{roundIndex:0000}.zip");
    }
}
