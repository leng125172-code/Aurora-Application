using AuroraStruct3D.DeviceState;
using AuroraStruct3D.Projectors.Dtos;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Entities;

namespace AuroraStruct3D.Projectors;

/// <summary>
/// 投影机设备管理及手动控制应用服务。
/// 所有硬件控制操作（连接/LED/显示/触发等）要求设备运行模式为手动或检修模式，
/// 由 <see cref="IDeviceStateManager"/> 进行运行模式校验。
/// </summary>
public class ProjectorDeviceAppService : AuroraStruct3DAppService, IProjectorDeviceAppService
{
    private readonly IProjectorDeviceRepository _projectorDeviceRepository;
    private readonly IProjectorOperationLogRepository _operationLogRepository;
    private readonly IProjectorConnectionPool _connectionPool;
    private readonly IDeviceStateManager _deviceStateManager;
    private readonly IDlpProjectorService _dlpProjectorService;

    public ProjectorDeviceAppService(
        IProjectorDeviceRepository projectorDeviceRepository,
        IProjectorOperationLogRepository operationLogRepository,
        IProjectorConnectionPool connectionPool,
        IDeviceStateManager deviceStateManager,
        IDlpProjectorService dlpProjectorService
    )
    {
        _projectorDeviceRepository = projectorDeviceRepository;
        _operationLogRepository = operationLogRepository;
        _connectionPool = connectionPool;
        _deviceStateManager = deviceStateManager;
        _dlpProjectorService = dlpProjectorService;
    }

    // ─── 设备 CRUD ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<PagedResultDto<ProjectorDeviceDto>> GetListAsync(GetProjectorListDto input)
    {
        List<ProjectorDevice> all = await _projectorDeviceRepository.GetListOrderedAsync();
        IEnumerable<ProjectorDevice> filtered = all;

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            string keyword = input.Filter.Trim().ToUpperInvariant();
            filtered = filtered.Where(x =>
                x.Name.ToUpperInvariant().Contains(keyword)
                || (x.IpAddress != null && x.IpAddress.Contains(keyword))
                || (x.Description != null && x.Description.ToUpperInvariant().Contains(keyword))
            );
        }

        if (input.IsEnabled.HasValue)
        {
            filtered = filtered.Where(x => x.IsEnabled == input.IsEnabled.Value);
        }

        List<ProjectorDevice> page = filtered
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount)
            .ToList();

        return new PagedResultDto<ProjectorDeviceDto>(
            filtered.Count(),
            page.Select(x => x.ToDto()).ToList()
        );
    }

    /// <inheritdoc/>
    public async Task<ProjectorDeviceDto> GetAsync(Guid id)
    {
        ProjectorDevice device = await _projectorDeviceRepository.GetAsync(id);
        return device.ToDto();
    }

    /// <inheritdoc/>
    public async Task<int> ScanProjectorsAsync()
    {
        // 探测当前连接的 HID 投影机数量
        int count = _dlpProjectorService.GetHidDeviceCount(
            ProjectorConsts.HidVendorId,
            ProjectorConsts.HidProductId
        );

        // 对每个检测到的 HID 索引，确保数据库中存在对应记录
        for (int i = 0; i < count; i++)
        {
            ProjectorDevice? existing = await _projectorDeviceRepository.FindByHidAsync(i);
            if (existing == null)
            {
                // 新设备：取当前设备数作为 DeviceIndex
                List<ProjectorDevice> all = await _projectorDeviceRepository.GetListOrderedAsync();
                int nextIndex = all.Count;
                ProjectorDevice device = new(
                    GuidGenerator.Create(),
                    $"投影机 {i}",
                    nextIndex,
                    i,
                    ProjectorConsts.DefaultConnectTimeoutMs
                );
                await _projectorDeviceRepository.InsertAsync(device);
            }
        }

        // 重建并传播 HidDeviceIndex -> ProjectorDevice.Id 映射
        List<ProjectorDevice> devices = await _projectorDeviceRepository.GetListOrderedAsync();
        Dictionary<int, Guid> mapping = devices
            .Where(d => d.ConnectionType == ProjectorConnectionType.UsbHid)
            .ToDictionary(d => d.HidDeviceIndex, d => d.Id);
        _dlpProjectorService.SetProjectorDeviceIdMapping(mapping);

        return count;
    }

    /// <inheritdoc/>
    public async Task<ProjectorDeviceDto> UpdateAsync(Guid id, UpdateProjectorDeviceDto input)
    {
        ProjectorDevice device = await _projectorDeviceRepository.GetAsync(id);
        device.SetName(input.Name);
        device.SetDescription(input.Description);
        if (input.IsEnabled)
            device.Enable();
        else
            device.Disable();

        await _projectorDeviceRepository.UpdateAsync(device);
        return device.ToDto();
    }

    // ─── 连接管理 ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task ConnectAsync(Guid id)
    {
        EnsureManualOrMaintenanceMode();
        ProjectorDevice device = await _projectorDeviceRepository.GetAsync(id);
        IDlpProjectorService svc = _connectionPool.GetOrCreate(id);

        try
        {
            if (device.ConnectionType == ProjectorConnectionType.Tcp)
            {
                await svc.ConnectAsync(device.IpAddress!, device.TcpPort);
            }
            else
            {
                // VID/PID 为硬件固定值（ProjectorConsts.HidVendorId/HidProductId），不存储于数据库
                await svc.ConnectHidAsync(
                    ProjectorConsts.HidVendorId,
                    ProjectorConsts.HidProductId,
                    device.HidDeviceIndex
                );
            }
        }
        catch (Exception ex)
        {
            // 连接失败：更新设备状态为已断开，并记录失败日志
            device.UpdateConnectionStatus(
                ProjectorConnectionStatus.Disconnected,
                disconnectedAt: DateTime.UtcNow
            );
            await _projectorDeviceRepository.UpdateAsync(device);

            await _operationLogRepository.InsertAsync(
                ProjectorOperationLog.Failure(
                    GuidGenerator.Create(),
                    id,
                    ProjectorOperationType.Connect,
                    ex.Message
                )
            );

            throw new UserFriendlyException($"连接投影机失败：{ex.Message}");
        }

        // 连接成功后更新实体状态
        device.UpdateConnectionStatus(
            ProjectorConnectionStatus.Connected,
            connectedAt: DateTime.UtcNow
        );
        await _projectorDeviceRepository.UpdateAsync(device);

        await _operationLogRepository.InsertAsync(
            ProjectorOperationLog.Success(
                GuidGenerator.Create(),
                id,
                ProjectorOperationType.Connect
            )
        );
    }

    /// <inheritdoc/>
    public async Task DisconnectAsync(Guid id)
    {
        EnsureManualOrMaintenanceMode();
        IDlpProjectorService? svc = _connectionPool.TryGet(id);
        if (svc != null)
        {
            await svc.DisconnectAsync();
        }

        ProjectorDevice device = await _projectorDeviceRepository.GetAsync(id);
        device.UpdateConnectionStatus(
            ProjectorConnectionStatus.Disconnected,
            disconnectedAt: DateTime.UtcNow
        );
        await _projectorDeviceRepository.UpdateAsync(device);

        await _operationLogRepository.InsertAsync(
            ProjectorOperationLog.Success(
                GuidGenerator.Create(),
                id,
                ProjectorOperationType.Disconnect
            )
        );
    }

    // ─── LED 控制 ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<bool> LedOnAsync(Guid id)
    {
        EnsureManualOrMaintenanceMode();
        IDlpProjectorService svc = GetConnectedService(id);
        bool ok = await svc.LedOnAsync();
        if (ok)
        {
            ProjectorDevice device = await _projectorDeviceRepository.GetAsync(id);
            device.UpdateLedStatus(ProjectorLedStatus.On);
            await _projectorDeviceRepository.UpdateAsync(device);
        }
        return ok;
    }

    /// <inheritdoc/>
    public async Task<bool> LedOffAsync(Guid id)
    {
        EnsureManualOrMaintenanceMode();
        IDlpProjectorService svc = GetConnectedService(id);
        bool ok = await svc.LedOffAsync();
        if (ok)
        {
            ProjectorDevice device = await _projectorDeviceRepository.GetAsync(id);
            device.UpdateLedStatus(ProjectorLedStatus.Off);
            await _projectorDeviceRepository.UpdateAsync(device);
        }
        return ok;
    }

    /// <inheritdoc/>
    public async Task<bool> SetLightAsync(SetProjectorLightDto input)
    {
        EnsureManualOrMaintenanceMode();
        IDlpProjectorService svc = GetConnectedService(input.ProjectorDeviceId);

        // 硬件限制：在红/绿/蓝颜色模式下直接改亮度不会生效，
        // 需要先切换到白色（LC），设置亮度后再恢复原颜色。
        ProjectorDevice device = await _projectorDeviceRepository.GetAsync(input.ProjectorDeviceId);
        ProjectorColor currentColor = device.LastColor;
        bool needsColorSwitch =
            currentColor is ProjectorColor.Red or ProjectorColor.Green or ProjectorColor.Blue;

        if (needsColorSwitch)
        {
            // 先切到白色（LC）
            bool switchOk = await svc.SetColorAsync(ProjectorColor.White);
            if (!switchOk)
                return false;
        }

        bool ok = await svc.SetLightAsync(input.Light);

        if (ok)
        {
            // 恢复原来的颜色
            if (needsColorSwitch)
            {
                await svc.SetColorAsync(currentColor);
            }
            device.UpdateLightValue(input.Light);
            await _projectorDeviceRepository.UpdateAsync(device);
        }
        return ok;
    }

    // ─── 显示控制 ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<bool> SetDisplayModeAsync(SetProjectorDisplayModeDto input)
    {
        EnsureManualOrMaintenanceMode();
        IDlpProjectorService svc = GetConnectedService(input.ProjectorDeviceId);
        bool ok = await svc.SetDisplayModeAsync(input.Mode);
        if (ok)
        {
            ProjectorDevice device = await _projectorDeviceRepository.GetAsync(
                input.ProjectorDeviceId
            );
            device.UpdateDisplayMode((byte)input.Mode);
            await _projectorDeviceRepository.UpdateAsync(device);
        }
        return ok;
    }

    /// <inheritdoc/>
    public async Task<bool> SetColorAsync(SetProjectorColorDto input)
    {
        EnsureManualOrMaintenanceMode();
        IDlpProjectorService svc = GetConnectedService(input.ProjectorDeviceId);
        bool ok = await svc.SetColorAsync(input.Color);
        if (ok)
        {
            ProjectorDevice device = await _projectorDeviceRepository.GetAsync(
                input.ProjectorDeviceId
            );
            device.UpdateColor(input.Color);
            await _projectorDeviceRepository.UpdateAsync(device);
        }
        return ok;
    }

    /// <inheritdoc/>
    public async Task<bool> SetFlipAsync(SetProjectorFlipDto input)
    {
        EnsureManualOrMaintenanceMode();
        IDlpProjectorService svc = GetConnectedService(input.ProjectorDeviceId);
        bool ok = await svc.SetFlipAsync(input.FlipMode);
        if (ok)
        {
            ProjectorDevice device = await _projectorDeviceRepository.GetAsync(
                input.ProjectorDeviceId
            );
            device.UpdateFlipMode(input.FlipMode);
            await _projectorDeviceRepository.UpdateAsync(device);
        }
        return ok;
    }

    /// <inheritdoc/>
    public async Task<bool> SetTriggerModeAsync(SetProjectorTriggerModeDto input)
    {
        EnsureManualOrMaintenanceMode();
        IDlpProjectorService svc = GetConnectedService(input.ProjectorDeviceId);
        bool ok = await svc.SetTriggerModeAsync(input.TriggerMode);
        if (ok)
        {
            ProjectorDevice device = await _projectorDeviceRepository.GetAsync(
                input.ProjectorDeviceId
            );
            device.UpdateTriggerMode(input.TriggerMode);
            await _projectorDeviceRepository.UpdateAsync(device);
        }
        return ok;
    }

    /// <inheritdoc/>
    public async Task<bool> SetBootImageAsync(SetProjectorBootImageDto input)
    {
        EnsureManualOrMaintenanceMode();
        IDlpProjectorService svc = GetConnectedService(input.ProjectorDeviceId);
        bool ok = await svc.SetBootImageAsync(input.BootImage);
        if (ok)
        {
            ProjectorDevice device = await _projectorDeviceRepository.GetAsync(
                input.ProjectorDeviceId
            );
            device.UpdateBootImage(input.BootImage);
            await _projectorDeviceRepository.UpdateAsync(device);
        }
        return ok;
    }

    /// <inheritdoc/>
    public async Task<bool> SetCheckerboardPixelSizeAsync(SetProjectorCheckerboardDto input)
    {
        EnsureManualOrMaintenanceMode();
        IDlpProjectorService svc = GetConnectedService(input.ProjectorDeviceId);
        bool ok = await svc.SetCheckerboardPixelSizeAsync(input.PixelSize);
        if (ok)
        {
            ProjectorDevice device = await _projectorDeviceRepository.GetAsync(
                input.ProjectorDeviceId
            );
            device.UpdateCheckerboardPixelSize(input.PixelSize);
            await _projectorDeviceRepository.UpdateAsync(device);
        }
        return ok;
    }

    /// <inheritdoc/>
    public async Task<bool> SetRgbColorAsync(SetProjectorRgbDto input)
    {
        EnsureManualOrMaintenanceMode();
        IDlpProjectorService svc = GetConnectedService(input.ProjectorDeviceId);
        bool ok = await svc.SetRgbColorAsync(input.R, input.G, input.B);
        if (ok)
        {
            ProjectorDevice device = await _projectorDeviceRepository.GetAsync(
                input.ProjectorDeviceId
            );
            device.UpdateLedRgb(input.R, input.G, input.B);
            // RGB 自定义色对应"神光同步（AuraSync）"颜色模式
            device.UpdateColor(ProjectorColor.AuraSync);
            await _projectorDeviceRepository.UpdateAsync(device);
        }
        return ok;
    }

    // ─── 条纹触发 ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<bool> TriggerOnceAsync(TriggerProjectorDto input)
    {
        EnsureManualOrMaintenanceMode();
        IDlpProjectorService svc = GetConnectedService(input.ProjectorDeviceId);
        return await svc.TriggerOnceAsync(input.EndGray);
    }

    // ─── 高级操作 ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<bool> SoftResetAsync(Guid id)
    {
        EnsureManualOrMaintenanceMode();
        IDlpProjectorService svc = GetConnectedService(id);
        return await svc.SoftResetAsync();
    }

    /// <inheritdoc/>
    public async Task<bool> SaveParamsAsync(Guid id)
    {
        EnsureManualOrMaintenanceMode();
        IDlpProjectorService svc = GetConnectedService(id);
        return await svc.SaveParamsAsync();
    }

    /// <inheritdoc/>
    public async Task<string?> ReadRegisterAsync(Guid id, int address)
    {
        EnsureManualOrMaintenanceMode();
        IDlpProjectorService svc = GetConnectedService(id);
        return await svc.ReadRegisterAsync(address);
    }

    /// <inheritdoc/>
    public async Task<bool> WriteRegisterAsync(WriteProjectorRegisterDto input)
    {
        EnsureManualOrMaintenanceMode();
        IDlpProjectorService svc = GetConnectedService(input.ProjectorDeviceId);
        return await svc.WriteRegisterAsync(input.Address, input.Value);
    }

    // ─── 操作日志 ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<PagedResultDto<ProjectorOperationLogDto>> GetLogsAsync(
        GetProjectorLogListDto input
    )
    {
        if (!input.ProjectorDeviceId.HasValue)
        {
            return new PagedResultDto<ProjectorOperationLogDto>(
                0,
                new List<ProjectorOperationLogDto>()
            );
        }

        Guid deviceId = input.ProjectorDeviceId.Value;
        long totalCount = await _operationLogRepository.GetCountAsync(
            deviceId,
            input.OperationType,
            input.IsFailedOnly ?? false
        );

        List<ProjectorOperationLog> logs = await _operationLogRepository.GetPagedListAsync(
            deviceId,
            input.SkipCount,
            input.MaxResultCount,
            input.OperationType,
            input.IsFailedOnly ?? false
        );

        return new PagedResultDto<ProjectorOperationLogDto>(
            totalCount,
            logs.Select(x => x.ToDto()).ToList()
        );
    }

    // ─── 辅助 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 校验当前设备运行模式是否为手动或检修模式；不满足时抛出 <see cref="UserFriendlyException"/>。
    /// 投影机手动控制接口专属校验，联机/自动模式下禁止执行。
    /// </summary>
    private void EnsureManualOrMaintenanceMode()
    {
        DeviceRunMode mode = _deviceStateManager.RunMode;
        if (mode is not (DeviceRunMode.Manual or DeviceRunMode.Maintenance))
        {
            throw new UserFriendlyException(
                $"当前运行模式为【{mode switch {
                    DeviceRunMode.Online => "联机",
                    DeviceRunMode.Auto   => "自动",
                    _                    => mode.ToString()
                }}】，投影机手动控制仅允许在手动模式或检修模式下执行"
            );
        }
    }

    /// <summary>
    /// 获取已连接的投影机服务实例，若无法找到则抛出异常
    /// </summary>
    private IDlpProjectorService GetConnectedService(Guid deviceId)
    {
        IDlpProjectorService? svc = _connectionPool.TryGet(deviceId);
        if (svc == null)
        {
            throw new UserFriendlyException("投影机尚未连接，请先调用连接接口");
        }
        return svc;
    }

    /// <summary>
    /// 检查 DeviceIndex 是否已被使用
    /// </summary>
    private async Task EnsureDeviceIndexUniqueAsync(int deviceIndex)
    {
        List<ProjectorDevice> all = await _projectorDeviceRepository.GetListOrderedAsync();
        if (all.Any(x => x.DeviceIndex == deviceIndex))
        {
            throw new UserFriendlyException($"设备序号 {deviceIndex} 已被占用");
        }
    }
}
