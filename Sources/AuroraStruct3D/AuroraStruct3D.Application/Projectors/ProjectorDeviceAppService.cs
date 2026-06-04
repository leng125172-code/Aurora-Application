using AuroraStruct3D.DeviceState;
using AuroraStruct3D.Projectors.Dtos;
using AuroraStruct3D.Sessions;
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
[Authorize]
public class ProjectorDeviceAppService : AuroraStruct3DAppService, IProjectorDeviceAppService
{
    private readonly IProjectorDeviceRepository _projectorDeviceRepository;
    private readonly IProjectorOperationLogRepository _operationLogRepository;
    private readonly IProjectorConnectionPool _connectionPool;
    private readonly IDeviceStateManager _deviceStateManager;
    private readonly IDlpProjectorService _dlpProjectorService;
    private readonly IDeviceOperationSessionManager _sessionManager;
    private readonly ICurrentClientSession _currentClientSession;
    private readonly IProjectorHubNotifier _projectorHubNotifier;
    private readonly ProjectorFringeDownloadStateStore _fringeDownloadStateStore;

    public ProjectorDeviceAppService(
        IProjectorDeviceRepository projectorDeviceRepository,
        IProjectorOperationLogRepository operationLogRepository,
        IProjectorConnectionPool connectionPool,
        IDeviceStateManager deviceStateManager,
        IDlpProjectorService dlpProjectorService,
        IDeviceOperationSessionManager sessionManager,
        ICurrentClientSession currentClientSession,
        IProjectorHubNotifier projectorHubNotifier,
        ProjectorFringeDownloadStateStore fringeDownloadStateStore
    )
    {
        _projectorDeviceRepository = projectorDeviceRepository;
        _operationLogRepository = operationLogRepository;
        _connectionPool = connectionPool;
        _deviceStateManager = deviceStateManager;
        _dlpProjectorService = dlpProjectorService;
        _sessionManager = sessionManager;
        _currentClientSession = currentClientSession;
        _projectorHubNotifier = projectorHubNotifier;
        _fringeDownloadStateStore = fringeDownloadStateStore;
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
        EnsureManualOrMaintenanceMode();
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
        EnsureManualOrMaintenanceMode();
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
        await EnsureOrAcquireSessionAsync(id, DeviceType.Projector);
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
        await EnsureOrAcquireSessionAsync(id, DeviceType.Projector);
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
        await EnsureOrAcquireSessionAsync(input.ProjectorDeviceId, DeviceType.Projector);
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
        await EnsureOrAcquireSessionAsync(input.ProjectorDeviceId, DeviceType.Projector);
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
        await EnsureOrAcquireSessionAsync(input.ProjectorDeviceId, DeviceType.Projector);
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
        await EnsureOrAcquireSessionAsync(input.ProjectorDeviceId, DeviceType.Projector);
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
        await EnsureOrAcquireSessionAsync(input.ProjectorDeviceId, DeviceType.Projector);
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
        await EnsureOrAcquireSessionAsync(input.ProjectorDeviceId, DeviceType.Projector);
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
        await EnsureOrAcquireSessionAsync(input.ProjectorDeviceId, DeviceType.Projector);
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
        await EnsureOrAcquireSessionAsync(input.ProjectorDeviceId, DeviceType.Projector);
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
        await EnsureOrAcquireSessionAsync(input.ProjectorDeviceId, DeviceType.Projector);
        IDlpProjectorService svc = GetConnectedService(input.ProjectorDeviceId);
        return await svc.TriggerOnceAsync(input.EndGray);
    }

    // ─── 高级操作 ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<bool> SoftResetAsync(Guid id)
    {
        EnsureManualOrMaintenanceMode();
        await EnsureOrAcquireSessionAsync(id, DeviceType.Projector);
        IDlpProjectorService svc = GetConnectedService(id);
        return await svc.SoftResetAsync();
    }

    /// <inheritdoc/>
    public async Task<bool> SaveParamsAsync(Guid id)
    {
        EnsureManualOrMaintenanceMode();
        await EnsureOrAcquireSessionAsync(id, DeviceType.Projector);
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
        await EnsureOrAcquireSessionAsync(input.ProjectorDeviceId, DeviceType.Projector);
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
            input.IsFailedOnly ?? false,
            input.StartTime,
            input.EndTime
        );

        List<ProjectorOperationLog> logs = await _operationLogRepository.GetPagedListAsync(
            deviceId,
            input.SkipCount,
            input.MaxResultCount,
            input.OperationType,
            input.IsFailedOnly ?? false,
            input.StartTime,
            input.EndTime
        );

        return new PagedResultDto<ProjectorOperationLogDto>(
            totalCount,
            logs.Select(x => x.ToDto()).ToList()
        );
    }

    // ─── 像素分辨率与条纹下载 ───────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<ProjectorPixelResolutionDto> GetPixelResolutionAsync(Guid id)
    {
        IDlpProjectorService svc = GetConnectedService(id);
        (int widthPixels, string pixelMode) = await svc.GetPixelResolutionAsync()
            .ConfigureAwait(false);
        return new ProjectorPixelResolutionDto { WidthPixels = widthPixels, PixelMode = pixelMode };
    }

    /// <inheritdoc/>
    public Task<List<FringePreviewImageDto>> GenerateFringePreviewAsync(
        DownloadFringePatternInputDto input
    )
    {
        byte[][] images = BuildFringeImagePixels(input);
        List<FringePreviewImageDto> result = images
            .Select(
                (pixels, index) =>
                    new FringePreviewImageDto
                    {
                        Index = index,
                        Label = $"图像 {index + 1}",
                        Pixels = pixels,
                    }
            )
            .ToList();

        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<ProjectorFringeDownloadStatusDto> GetFringeDownloadStatusAsync(Guid id)
    {
        return Task.FromResult(_fringeDownloadStateStore.Get(id));
    }

    /// <inheritdoc/>
    public Task DownloadFringePatternAsync(DownloadFringePatternInputDto input)
    {
        bool isVertical = input.FringeMode.Equals("vertical", StringComparison.OrdinalIgnoreCase);
        byte[][] images = BuildFringeImagePixels(input);
        int pixelCount = images[0].Length;
        byte[] columnGrayValues = new byte[input.ImageCount * pixelCount];
        for (int i = 0; i < images.Length; i++)
        {
            Buffer.BlockCopy(images[i], 0, columnGrayValues, i * pixelCount, pixelCount);
        }

        if (
            !_fringeDownloadStateStore.TryStart(
                input.ProjectorId,
                out ProjectorFringeDownloadStatusDto startedStatus
            )
        )
        {
            throw new UserFriendlyException("当前投影机已有条纹下载任务正在执行，请勿重复发起。");
        }

        _ = Task.Run(async () =>
        {
            try
            {
                IDlpProjectorService svc = GetConnectedService(input.ProjectorId);
                await _projectorHubNotifier
                    .NotifyFringeDownloadStatusChangedAsync(startedStatus)
                    .ConfigureAwait(false);
                await _projectorHubNotifier
                    .NotifyFringeDownloadProgressAsync(input.ProjectorId, 0)
                    .ConfigureAwait(false);

                async Task reportProgress(int pct)
                {
                    try
                    {
                        ProjectorFringeDownloadStatusDto runningStatus =
                            _fringeDownloadStateStore.UpdateProgress(input.ProjectorId, pct);
                        await _projectorHubNotifier
                            .NotifyFringeDownloadStatusChangedAsync(runningStatus)
                            .ConfigureAwait(false);
                        await _projectorHubNotifier
                            .NotifyFringeDownloadProgressAsync(input.ProjectorId, pct)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        // 进度推送失败不中断下载流程
                    }
                }

                await svc.DownloadFringePatternAsync(
                        input.ImageCount,
                        columnGrayValues,
                        !isVertical,
                        reportProgress
                    )
                    .ConfigureAwait(false);

                ProjectorFringeDownloadStatusDto completedStatus =
                    _fringeDownloadStateStore.Complete(input.ProjectorId);
                await _projectorHubNotifier
                    .NotifyFringeDownloadStatusChangedAsync(completedStatus)
                    .ConfigureAwait(false);
                await _projectorHubNotifier
                    .NotifyFringeDownloadProgressAsync(input.ProjectorId, 100)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(
                    ex,
                    "[Projector] Fringe download failed for projector {ProjectorId}",
                    input.ProjectorId
                );
                ProjectorFringeDownloadStatusDto failedStatus = _fringeDownloadStateStore.Fail(
                    input.ProjectorId,
                    ex.Message
                );
                try
                {
                    await _projectorHubNotifier
                        .NotifyFringeDownloadStatusChangedAsync(failedStatus)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // 状态推送失败不再抛出，避免后台任务崩溃。
                }
            }
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// 按与设备下载完全一致的规则生成条纹图一维像素数据。
    /// 竖条纹：每张图长度为 WidthPixels；横条纹：每张图长度为 HeightPixels。
    /// </summary>
    private static byte[][] BuildFringeImagePixels(DownloadFringePatternInputDto input)
    {
        bool isVertical = input.FringeMode.Equals("vertical", StringComparison.OrdinalIgnoreCase);
        int pixelCount = isVertical ? input.WidthPixels : input.HeightPixels;

        if (pixelCount <= 0)
        {
            throw new UserFriendlyException("条纹像素数无效，无法生成预览图像。");
        }

        if (pixelCount % input.PeriodCount != 0)
        {
            throw new UserFriendlyException("条纹周期数必须能整除有效像素数。");
        }

        if (input.PhaseShift <= 0 || input.PhaseShift >= input.PeriodCount)
        {
            throw new UserFriendlyException("相移量必须大于 0 且小于周期数。");
        }

        int stripeWidth = pixelCount / input.PeriodCount;
        byte firstColor = input.FringeType.Equals("wb", StringComparison.OrdinalIgnoreCase)
            ? (byte)255
            : (byte)0;
        byte secondColor = (byte)(255 - firstColor);

        byte[][] images = new byte[input.ImageCount][];
        for (int i = 0; i < input.ImageCount; i++)
        {
            byte[] pixels = new byte[pixelCount];
            int offset = i * input.PhaseShift;
            for (int pos = 0; pos < pixelCount; pos++)
            {
                int shifted = (pos + offset) % pixelCount;
                int stripeIdx = (shifted / stripeWidth) % 2;
                pixels[pos] = stripeIdx == 0 ? firstColor : secondColor;
            }
            images[i] = pixels;
        }

        return images;
    }

    // ─── 辅助 ─────────────────────────────────────────────────────────────

    /// <summary>    /// 检查并获取设备独占会话。
    /// </summary>
    private Task EnsureOrAcquireSessionAsync(Guid deviceId, DeviceType deviceType)
    {
        string? clientSessionId = _currentClientSession.SessionId;
        if (string.IsNullOrWhiteSpace(clientSessionId))
        {
            return Task.CompletedTask;
        }

        string? userId = CurrentUser.Id?.ToString();
        string userName = CurrentUser.Name ?? CurrentUser.UserName ?? clientSessionId[..8] + "...";

        _sessionManager.TryAcquire(
            deviceId,
            deviceType,
            clientSessionId,
            userId,
            userName,
            force: false
        );

        return Task.CompletedTask;
    }

    /// <summary>    /// 校验当前设备运行模式是否为手动或检修模式；不满足时抛出 <see cref="UserFriendlyException"/>。
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
