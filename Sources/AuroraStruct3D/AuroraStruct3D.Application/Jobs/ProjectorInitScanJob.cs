using AuroraStruct3D.Projectors;
using AuroraStruct3D.Projectors.Dtos;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundJobs;

namespace AuroraStruct3D.Jobs;

/// <summary>
/// 投影仪初始化扫描 Job 参数（无参，仅用于类型标识）
/// </summary>
public class ProjectorInitScanJobArgs { }

/// <summary>
/// 应用启动后触发的投影仪初始化扫描 Hangfire Job。
/// 负责：枚举 USB HID 投影仪 → 同步数据库记录 → 依次连接每台已启用投影仪 → 开灯 → 颜色自检序列 → 恢复数据库参数。
/// </summary>
public class ProjectorInitScanJob
    : AsyncBackgroundJob<ProjectorInitScanJobArgs>,
        ITransientDependency
{
    private readonly IProjectorDeviceAppService _projectorDeviceAppService;
    private readonly IProjectorDeviceRepository _projectorDeviceRepository;
    private readonly ILogger<ProjectorInitScanJob> _logger;

    public ProjectorInitScanJob(
        IProjectorDeviceAppService projectorDeviceAppService,
        IProjectorDeviceRepository projectorDeviceRepository,
        ILogger<ProjectorInitScanJob> logger
    )
    {
        _projectorDeviceAppService = projectorDeviceAppService;
        _projectorDeviceRepository = projectorDeviceRepository;
        _logger = logger;
    }

    /// <summary>
    /// 执行投影仪初始化扫描：枚举 HID 设备，同步数据库记录，依次连接、开灯、颜色自检、恢复参数。
    /// </summary>
    public override async Task ExecuteAsync(ProjectorInitScanJobArgs args)
    {
        // ── 第一步：扫描 HID 设备，同步数据库记录 ──
        try
        {
            _logger.LogInformation("[ProjectorInitScanJob] 开始扫描 USB HID 投影仪...");
            int count = await _projectorDeviceAppService.ScanProjectorsAsync();
            _logger.LogInformation(
                "[ProjectorInitScanJob] 扫描完成，检测到 {Count} 台投影仪。",
                count
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ProjectorInitScanJob] 投影仪扫描失败：{Message}", ex.Message);
            return;
        }

        // ── 第二步：依次初始化每台已启用投影仪 ──
        List<ProjectorDevice> projectors = await _projectorDeviceRepository.GetEnabledListAsync();
        _logger.LogInformation(
            "[ProjectorInitScanJob] 开始初始化 {Count} 台已启用投影仪...",
            projectors.Count
        );
        foreach (ProjectorDevice device in projectors)
        {
            await InitProjectorAsync(device);
        }
    }

    /// <summary>
    /// 对单台投影仪执行：连接 → 开灯 → 颜色自检序列（R/G/B/White）→ 恢复数据库保存的亮度和颜色。
    /// 任一步骤失败时记录日志并跳过该设备，不影响其他设备初始化。
    /// </summary>
    private async Task InitProjectorAsync(ProjectorDevice device)
    {
        // ── 连接 ──
        try
        {
            _logger.LogInformation(
                "[ProjectorInitScanJob] 正在连接投影仪「{Name}」...",
                device.Name
            );
            // 复用应用服务连接入口：USB HID 设备会在连接后再次读取
            // 寄存器 0 校验身份，禁止启动任务绕过稳定身份保护。
            await _projectorDeviceAppService.ConnectAsync(device.Id);
            _logger.LogInformation(
                "[ProjectorInitScanJob] 投影仪「{Name}」连接成功。",
                device.Name
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "[ProjectorInitScanJob] 投影仪「{Name}」连接失败：{Message}",
                device.Name,
                ex.Message
            );
            // ConnectAsync 已使用新实体更新断开状态；这里不能再次保存启动扫描前
            // 加载的旧实体，否则 ConcurrencyStamp 已变化时会触发乐观并发异常。
            return;
        }

        // ── 开灯 ──
        try
        {
            bool ok = await _projectorDeviceAppService.LedOnAsync(device.Id);
            if (ok)
            {
                _logger.LogInformation(
                    "[ProjectorInitScanJob] 投影仪「{Name}」已开灯。",
                    device.Name
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "[ProjectorInitScanJob] 投影仪「{Name}」开灯失败：{Message}",
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
                await _projectorDeviceAppService.SetColorAsync(
                    new SetProjectorColorDto
                    {
                        ProjectorDeviceId = device.Id,
                        Color = color,
                    }
                );
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "[ProjectorInitScanJob] 投影仪「{Name}」颜色自检步骤 {Color} 失败：{Message}",
                    device.Name,
                    color,
                    ex.Message
                );
            }
        }

        // ── 恢复数据库保存的亮度和颜色 ──
        try
        {
            // AuraSync 为前端虚拟颜色模式，不是投影仪原生命令，恢复时回退到白光
            ProjectorColor targetColor =
                device.LastColor is ProjectorColor.AuraSync
                    ? ProjectorColor.White
                    : device.LastColor;

            // 亮度 0 是合法配置（关闭亮度），必须按数据库原值恢复，不能回退为默认值。
            byte targetLight = device.LastLightValue;
            await _projectorDeviceAppService.SetLightAsync(
                new SetProjectorLightDto
                {
                    ProjectorDeviceId = device.Id,
                    Light = targetLight,
                }
            );
            await _projectorDeviceAppService.SetColorAsync(
                new SetProjectorColorDto
                {
                    ProjectorDeviceId = device.Id,
                    Color = targetColor,
                }
            );

            _logger.LogInformation(
                "[ProjectorInitScanJob] 投影仪「{Name}」参数恢复完成：颜色={Color}，亮度={Light}。",
                device.Name,
                targetColor,
                targetLight
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "[ProjectorInitScanJob] 投影仪「{Name}」恢复参数失败：{Message}",
                device.Name,
                ex.Message
            );
        }
    }
}
