using AuroraStruct3D.Cameras;
using AuroraStruct3D.Cameras.Dtos;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundJobs;

namespace AuroraStruct3D.Jobs;

/// <summary>
/// 相机初始化扫描 Job 参数（无参，仅用于类型标识）
/// </summary>
public class CameraInitScanJobArgs { }

/// <summary>
/// 应用启动后触发的相机初始化扫描 Hangfire Job。
/// 负责：SDK 初始化 → 枚举实体相机 → 同步数据库记录 → 自动打开所有已启用相机 → 恢复激活参数集。
/// </summary>
public class CameraInitScanJob : AsyncBackgroundJob<CameraInitScanJobArgs>, ITransientDependency
{
    private readonly ICameraDeviceAppService _cameraDeviceAppService;
    private readonly ICameraDeviceRepository _cameraDeviceRepository;
    private readonly ILogger<CameraInitScanJob> _logger;

    public CameraInitScanJob(
        ICameraDeviceAppService cameraDeviceAppService,
        ICameraDeviceRepository cameraDeviceRepository,
        ILogger<CameraInitScanJob> logger
    )
    {
        _cameraDeviceAppService = cameraDeviceAppService;
        _cameraDeviceRepository = cameraDeviceRepository;
        _logger = logger;
    }

    /// <summary>
    /// 执行相机初始化扫描：扫描相机设备并自动打开所有已启用相机，然后恢复激活参数集。
    /// </summary>
    public override async Task ExecuteAsync(CameraInitScanJobArgs args)
    {
        // ── 第一步：扫描相机 ──
        try
        {
            _logger.LogInformation("[CameraInitScanJob] 开始扫描相机设备...");
            int count = await _cameraDeviceAppService.ScanCamerasAsync();
            _logger.LogInformation("[CameraInitScanJob] 扫描完成，检测到 {Count} 台相机。", count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CameraInitScanJob] 相机初始化扫描失败：{Message}", ex.Message);
            return;
        }

        // ── 第二步：打开已启用相机并恢复激活参数集 ──
        List<CameraDevice> cameras = await _cameraDeviceRepository.GetEnabledListAsync();
        int openedCount = 0;
        foreach (CameraDevice camera in cameras)
        {
            try
            {
                await _cameraDeviceAppService.OpenCameraAsync(camera.Id);
                openedCount++;
                _logger.LogInformation(
                    "[CameraInitScanJob] 相机「{Name}」已打开（Index={Index}）。",
                    camera.Name,
                    camera.DeviceIndex
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "[CameraInitScanJob] 相机「{Name}」打开失败，跳过参数集恢复：{Message}",
                    camera.Name,
                    ex.Message
                );
                continue;
            }

            if (camera.ActiveParameterSetId is null)
            {
                continue;
            }

            try
            {
                await _cameraDeviceAppService.ApplyParameterSetAsync(
                    camera.Id,
                    new ApplyCameraParameterSetDto
                    {
                        ParameterSetId = camera.ActiveParameterSetId.Value,
                    }
                );
                _logger.LogInformation(
                    "[CameraInitScanJob] 相机「{Name}」参数集已恢复（SetId={SetId}）。",
                    camera.Name,
                    camera.ActiveParameterSetId.Value
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "[CameraInitScanJob] 相机「{Name}」恢复参数集失败：{Message}",
                    camera.Name,
                    ex.Message
                );
            }
        }

        _logger.LogInformation(
            "[CameraInitScanJob] 启动初始化完成，已启用 {EnabledCount} 台，成功打开 {OpenedCount} 台。",
            cameras.Count,
            openedCount
        );
    }
}
