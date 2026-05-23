using AuroraStruct3D.Projectors;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundJobs;

namespace AuroraStruct3D.Jobs;

/// <summary>
/// 投影机初始化扫描 Job 参数（无参，仅用于类型标识）
/// </summary>
public class ProjectorInitScanJobArgs { }

/// <summary>
/// 应用启动后触发的投影机初始化扫描 Hangfire Job。
/// 负责：枚举 USB HID 投影机 → 同步数据库记录 → 依次连接每台已启用投影机 → 开灯 → 颜色自检序列 → 恢复数据库参数。
/// </summary>
public class ProjectorInitScanJob
    : AsyncBackgroundJob<ProjectorInitScanJobArgs>,
        ITransientDependency
{
    private readonly IProjectorDeviceAppService _projectorDeviceAppService;
    private readonly IProjectorDeviceRepository _projectorDeviceRepository;
    private readonly IProjectorConnectionPool _connectionPool;
    private readonly ILogger<ProjectorInitScanJob> _logger;

    public ProjectorInitScanJob(
        IProjectorDeviceAppService projectorDeviceAppService,
        IProjectorDeviceRepository projectorDeviceRepository,
        IProjectorConnectionPool connectionPool,
        ILogger<ProjectorInitScanJob> logger
    )
    {
        _projectorDeviceAppService = projectorDeviceAppService;
        _projectorDeviceRepository = projectorDeviceRepository;
        _connectionPool = connectionPool;
        _logger = logger;
    }

    /// <summary>
    /// 执行投影机初始化扫描：枚举 HID 设备，同步数据库记录，依次连接、开灯、颜色自检、恢复参数。
    /// </summary>
    public override async Task ExecuteAsync(ProjectorInitScanJobArgs args)
    {
        // ── 第一步：扫描 HID 设备，同步数据库记录 ──
        try
        {
            _logger.LogInformation("[ProjectorInitScanJob] 开始扫描 USB HID 投影机...");
            int count = await _projectorDeviceAppService.ScanProjectorsAsync();
            _logger.LogInformation(
                "[ProjectorInitScanJob] 扫描完成，检测到 {Count} 台投影机。",
                count
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ProjectorInitScanJob] 投影机扫描失败：{Message}", ex.Message);
            return;
        }

        // ── 第二步：依次初始化每台已启用投影机 ──
        List<ProjectorDevice> projectors = await _projectorDeviceRepository.GetEnabledListAsync();
        _logger.LogInformation(
            "[ProjectorInitScanJob] 开始初始化 {Count} 台已启用投影机...",
            projectors.Count
        );
        foreach (ProjectorDevice device in projectors)
        {
            await InitProjectorAsync(device);
        }
    }

    /// <summary>
    /// 对单台投影机执行：连接 → 开灯 → 颜色自检序列（R/G/B/White）→ 恢复数据库保存的亮度和颜色。
    /// 任一步骤失败时记录日志并跳过该设备，不影响其他设备初始化。
    /// </summary>
    private async Task InitProjectorAsync(ProjectorDevice device)
    {
        IDlpProjectorService svc = _connectionPool.GetOrCreate(device.Id);

        // ── 连接 ──
        try
        {
            _logger.LogInformation(
                "[ProjectorInitScanJob] 正在连接投影机「{Name}」...",
                device.Name
            );
            if (device.ConnectionType == ProjectorConnectionType.Tcp)
            {
                await svc.ConnectAsync(device.IpAddress!, device.TcpPort);
            }
            else
            {
                await svc.ConnectHidAsync(
                    ProjectorConsts.HidVendorId,
                    ProjectorConsts.HidProductId,
                    device.HidDeviceIndex
                );
            }
            device.UpdateConnectionStatus(
                ProjectorConnectionStatus.Connected,
                connectedAt: DateTime.UtcNow
            );
            await _projectorDeviceRepository.UpdateAsync(device);
            _logger.LogInformation(
                "[ProjectorInitScanJob] 投影机「{Name}」连接成功。",
                device.Name
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "[ProjectorInitScanJob] 投影机「{Name}」连接失败：{Message}",
                device.Name,
                ex.Message
            );
            device.UpdateConnectionStatus(
                ProjectorConnectionStatus.Disconnected,
                disconnectedAt: DateTime.UtcNow
            );
            await _projectorDeviceRepository.UpdateAsync(device);
            return;
        }

        // ── 开灯 ──
        try
        {
            bool ok = await svc.LedOnAsync();
            if (ok)
            {
                device.UpdateLedStatus(ProjectorLedStatus.On);
                await _projectorDeviceRepository.UpdateAsync(device);
                _logger.LogInformation(
                    "[ProjectorInitScanJob] 投影机「{Name}」已开灯。",
                    device.Name
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "[ProjectorInitScanJob] 投影机「{Name}」开灯失败：{Message}",
                device.Name,
                ex.Message
            );
        }

        // ── 颜色自检序列（Red → Green → Blue → White，各保持约 1 秒）──
        ProjectorColor[] checkSequence =
        [
            ProjectorColor.Red,
            ProjectorColor.Green,
            ProjectorColor.Blue,
            ProjectorColor.White,
        ];
        foreach (ProjectorColor color in checkSequence)
        {
            try
            {
                await svc.SetColorAsync(color);
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "[ProjectorInitScanJob] 投影机「{Name}」颜色自检步骤 {Color} 失败：{Message}",
                    device.Name,
                    color,
                    ex.Message
                );
            }
        }

        // ── 恢复数据库保存的亮度和颜色 ──
        try
        {
            // AuraSync 为前端虚拟颜色模式，不是投影机原生命令，恢复时回退到白光
            ProjectorColor targetColor =
                device.LastColor is ProjectorColor.AuraSync
                    ? ProjectorColor.White
                    : device.LastColor;

            // 亮度当前处于白光模式，可直接写入
            byte targetLight = device.LastLightValue > 0 ? device.LastLightValue : (byte)75;
            await svc.SetLightAsync(targetLight);
            await svc.SetColorAsync(targetColor);

            device.UpdateLightValue(targetLight);
            device.UpdateColor(targetColor);
            await _projectorDeviceRepository.UpdateAsync(device);

            _logger.LogInformation(
                "[ProjectorInitScanJob] 投影机「{Name}」参数恢复完成：颜色={Color}，亮度={Light}。",
                device.Name,
                targetColor,
                targetLight
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "[ProjectorInitScanJob] 投影机「{Name}」恢复参数失败：{Message}",
                device.Name,
                ex.Message
            );
        }
    }
}
