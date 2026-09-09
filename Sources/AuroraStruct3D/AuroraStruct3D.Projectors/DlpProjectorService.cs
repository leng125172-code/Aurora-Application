using System.Diagnostics;
using System.Text;
using AuroraStruct3D.Projectors.Protocol;
using HidSharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AuroraStruct3D.Projectors;

/// <summary>
/// 腾聚（TJ）结构光投影仪控制服务实现。
/// 支持 TCP/IP 和 USB HID 两种接口，ASCII 命令协议。
/// </summary>
public class DlpProjectorService : IDlpProjectorService, IDisposable
{
    private const string LogTag = "[Projector]";

    private readonly ILogger<DlpProjectorService> _logger;
    private readonly IServiceScopeFactory? _serviceScopeFactory;
    // The wire protocol is stateful.  Keep one operation lane per pooled
    // projector so a reconnect, a manual command and a multi-command download
    // cannot corrupt each other's request/response sequence.
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly AsyncLocal<int> _operationDepth = new();

    // ── TCP 客户端状态 ────────────────────────────────────────────
    private TjProjectorTcpClient? _tcpClient;
    private string _currentIp = string.Empty;
    private int _currentPort = TjProjectorCommands.TcpPort;

    // ── USB HID 客户端状态 ────────────────────────────────────────
    private TjProjectorHidClient? _hidClient;
    private int _currentHidVid;
    private int _currentHidPid;
    private int _currentHidIndex;

    // ── 当前连接方式 ──────────────────────────────────────────────
    private bool _useHid;

    // ── 关联的数据库设备 ID（用于操作日志写入） ─────────────────────
    private Guid _projectorDeviceId = Guid.Empty;

    // ── HID 设备索引到数据库设备 ID 映射（用于自动绑定日志设备）────────
    private IReadOnlyDictionary<int, Guid> _projectorDeviceIdMapping = new Dictionary<int, Guid>();

    public DlpProjectorService(
        ILogger<DlpProjectorService> logger,
        IServiceScopeFactory? serviceScopeFactory = null
    )
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
    }

    /// <inheritdoc/>
    public void SetProjectorDeviceId(Guid projectorDeviceId)
    {
        _projectorDeviceId = projectorDeviceId;
    }

    /// <inheritdoc/>
    public void SetProjectorDeviceIdMapping(IReadOnlyDictionary<int, Guid> deviceIds)
    {
        _projectorDeviceIdMapping = deviceIds ?? new Dictionary<int, Guid>();
        _logger.LogInformation(
            "{Tag} DlpProjectorService device-ID mapping injected, total {Count} HID indexes",
            LogTag,
            _projectorDeviceIdMapping.Count
        );
    }

    /// <inheritdoc/>
    public int GetHidDeviceCount(int vendorId, int productId)
    {
        try
        {
            return DeviceList.Local.GetHidDevices(vendorId, productId).Count();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "{Tag} GetHidDeviceCount(VID=0x{Vid:X4} PID=0x{Pid:X4}) 枚举失败",
                LogTag,
                vendorId,
                productId
            );
            return 0;
        }
    }

    /// <inheritdoc/>
    public Task ConnectAsync(string ip, CancellationToken cancellationToken = default)
    {
        return ConnectAsync(ip, TjProjectorCommands.TcpPort, cancellationToken);
    }

    /// <inheritdoc/>
    public Task ConnectAsync(
        string ip,
        int port,
        CancellationToken cancellationToken = default
    )
    {
        return ExecuteOperationAsync(() => ConnectCoreAsync(ip, port, cancellationToken), cancellationToken);
    }

    private async Task ConnectCoreAsync(
        string ip,
        int port,
        CancellationToken cancellationToken
    )
    {
        if (
            !_useHid
            && _tcpClient != null
            && _currentIp == ip
            && _currentPort == port
            && _tcpClient.IsConnected
        )
        {
            _logger.LogDebug(
                "{Tag} Projector {Ip}:{Port} already connected, skipping duplicate connect",
                LogTag,
                ip,
                port
            );
            return;
        }

        await CloseCurrentClientAsync().ConfigureAwait(false);

        _currentIp = ip;
        _currentPort = port;
        _useHid = false;
        _tcpClient = new TjProjectorTcpClient(ip, port, _logger);
        await _tcpClient.ConnectAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task ConnectHidAsync(
        int vendorId = 0x0483,
        int productId = 0x5750,
        int deviceIndex = 0,
        CancellationToken cancellationToken = default
    )
    {
        return ExecuteOperationAsync(
            () => ConnectHidCoreAsync(vendorId, productId, deviceIndex, cancellationToken),
            cancellationToken
        );
    }

    private async Task ConnectHidCoreAsync(
        int vendorId,
        int productId,
        int deviceIndex,
        CancellationToken cancellationToken
    )
    {
        if (
            _useHid
            && _hidClient != null
            && _currentHidVid == vendorId
            && _currentHidPid == productId
            && _currentHidIndex == deviceIndex
            && _hidClient.IsConnected
        )
        {
            _logger.LogDebug(
                "{Tag} Projector HID 0x{Vid:X4}/0x{Pid:X4}[{Idx}] already connected, skipping duplicate connect",
                LogTag,
                vendorId,
                productId,
                deviceIndex
            );
            return;
        }

        await CloseCurrentClientAsync().ConfigureAwait(false);

        _currentHidVid = vendorId;
        _currentHidPid = productId;
        _currentHidIndex = deviceIndex;
        _useHid = true;
        _hidClient = new TjProjectorHidClient(vendorId, productId, _logger, deviceIndex);
        await _hidClient.ConnectAsync(cancellationToken).ConfigureAwait(false);

        // 连接成功后按 HID 索引自动绑定数据库设备 ID，供操作日志落库使用。
        if (_projectorDeviceIdMapping.TryGetValue(deviceIndex, out Guid deviceId))
        {
            _projectorDeviceId = deviceId;
        }
        else
        {
            _projectorDeviceId = Guid.Empty;
        }
    }

    /// <summary>关闭当前客户端（TCP 或 HID）</summary>
    private async Task CloseCurrentClientAsync()
    {
        if (_tcpClient != null)
        {
            await _tcpClient.DisconnectAsync().ConfigureAwait(false);
            _tcpClient.Dispose();
            _tcpClient = null;
        }
        if (_hidClient != null)
        {
            await _hidClient.DisconnectAsync().ConfigureAwait(false);
            _hidClient.Dispose();
            _hidClient = null;
        }
    }

    /// <inheritdoc/>
    public Task DisconnectAsync()
    {
        return ExecuteOperationAsync(DisconnectCoreAsync, CancellationToken.None);
    }

    private async Task DisconnectCoreAsync()
    {
        await CloseCurrentClientAsync().ConfigureAwait(false);
        _logger.LogInformation(
            _useHid
                ? "{Tag} Projector HID 0x{Vid:X4}/0x{Pid:X4} disconnected"
                : "{Tag} Projector {Ip} disconnected",
            LogTag,
            _useHid ? (object)_currentHidVid : _currentIp,
            _useHid ? (object)_currentHidPid : string.Empty
        );
    }

    /// <inheritdoc/>
    public async Task<DlpProjectorStatus> GetStatusAsync(
        CancellationToken cancellationToken = default
    )
    {
        bool connected = _useHid
            ? _hidClient?.IsConnected == true
            : _tcpClient?.IsConnected == true;
        string? version = null;

        if (connected)
        {
            version = await GetFirmwareVersionAsync(cancellationToken).ConfigureAwait(false);
        }

        return new DlpProjectorStatus
        {
            ConnectionType = _useHid ? "UsbHid" : "Tcp",
            IpAddress = _useHid ? null : _currentIp,
            Port = _useHid ? 0 : _currentPort,
            HidDevicePath = _useHid ? $"HID 0x{_currentHidVid:X4}/0x{_currentHidPid:X4}" : null,
            HidVendorId = _useHid ? _currentHidVid : 0,
            HidProductId = _useHid ? _currentHidPid : 0,
            IsConnected = connected,
            FirmwareVersion = version,
        };
    }

    /// <inheritdoc/>
    public async Task<string?> GetFirmwareVersionAsync(
        CancellationToken cancellationToken = default
    )
    {
        EnsureClient();
        return await SendCommandAndReadCoreAsync(TjProjectorCommands.ReadVersion, cancellationToken)
            .ConfigureAwait(false);
    }

    // ─── LED 控制 ──────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<bool> LedOnAsync(CancellationToken cancellationToken = default)
    {
        EnsureClient();
        _logger.LogInformation("{Tag} [Device {Device}] LED on", LogTag, DeviceId);
        bool ok = await SendAndLogAsync(
                TjProjectorCommands.LedOn,
                ProjectorOperationType.LedOn,
                null,
                cancellationToken
            )
            .ConfigureAwait(false);
        if (ok)
        {
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
        return ok;
    }

    /// <inheritdoc/>
    public async Task<bool> LedOffAsync(CancellationToken cancellationToken = default)
    {
        EnsureClient();
        _logger.LogInformation("{Tag} [Device {Device}] LED off", LogTag, DeviceId);
        bool ok = await SendAndLogAsync(
                TjProjectorCommands.LedOff,
                ProjectorOperationType.LedOff,
                null,
                cancellationToken
            )
            .ConfigureAwait(false);
        if (ok)
        {
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
        return ok;
    }

    /// <inheritdoc/>
    public async Task<bool> SetLightAsync(byte light, CancellationToken cancellationToken = default)
    {
        EnsureClient();

        if (light is > 175)
        {
            throw new ArgumentOutOfRangeException(
                nameof(light),
                "Brightness must be in range 0~175"
            );
        }

        _logger.LogInformation(
            "{Tag} [Device {Device}] Set brightness to {Light}",
            LogTag,
            DeviceId,
            light
        );

        string cmd = $"{TjProjectorCommands.SetLightPrefix}{light}";
        bool ok = await SendAndLogAsync(
                cmd,
                ProjectorOperationType.SetLight,
                $"Brightness={light}",
                cancellationToken
            )
            .ConfigureAwait(false);
        if (ok)
        {
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
        return ok;
    }

    // ─── 显示内容控制 ─────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<bool> SetDisplayModeAsync(
        ProjectorDisplayMode mode,
        CancellationToken cancellationToken = default
    )
    {
        EnsureClient();
        _logger.LogInformation(
            "{Tag} [Device {Device}] Set display mode: {Mode}",
            LogTag,
            DeviceId,
            mode
        );

        // 固件在 B1/B2 触发播放状态下不会响应 S0~S7。
        // 先切回 B0，让当前触发播放退出，再切换内置显示模式。
        bool stopped = await SetTriggerModeAsync(
                ProjectorTriggerMode.Normal,
                cancellationToken
            )
            .ConfigureAwait(false);
        if (!stopped)
        {
            return false;
        }
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);

        // 模式命令：'S' + ('0' + mode) + '\r' + '\n'（源码：TJSTPrjSetMode）
        char modeChar = (char)(TjProjectorCommands.SetModeOffsetBase + (byte)mode);
        string cmd = $"S{modeChar}\r\n";
        return await SendAndLogAsync(
                cmd,
                ProjectorOperationType.SetDisplayMode,
                $"Mode={mode}",
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> SetColorAsync(
        ProjectorColor color,
        CancellationToken cancellationToken = default
    )
    {
        EnsureClient();
        _logger.LogInformation(
            "{Tag} [Device {Device}] Set color: {Color}",
            LogTag,
            DeviceId,
            color
        );

        string cmd = color switch
        {
            ProjectorColor.Red => TjProjectorCommands.ColorRed,
            ProjectorColor.Green => TjProjectorCommands.ColorGreen,
            ProjectorColor.Blue => TjProjectorCommands.ColorBlue,
            ProjectorColor.White => TjProjectorCommands.ColorWhite,
            _ => TjProjectorCommands.ColorWhite,
        };
        return await SendAndLogAsync(
                cmd,
                ProjectorOperationType.SetColor,
                $"Color={color}",
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    // ─── 条纹投影触发 ─────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<bool> TriggerOnceAsync(CancellationToken cancellationToken = default)
    {
        EnsureClient();
        _logger.LogInformation(
            "{Tag} [Device {Device}] Trigger one-shot pattern (white ending)",
            LogTag,
            DeviceId
        );
        // nGray == 255 时使用 "T\r\n" 快速命令（源码：TJSTPrjTriggerOnce）
        return await SendAndLogAsync(
                TjProjectorCommands.TriggerOnce,
                ProjectorOperationType.TriggerOnce,
                "EndGray=255(white)",
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> TriggerOnceAsync(
        byte endGray,
        CancellationToken cancellationToken = default
    )
    {
        EnsureClient();
        _logger.LogInformation(
            "{Tag} [Device {Device}] Trigger one-shot pattern (end gray={Gray})",
            LogTag,
            DeviceId,
            endGray
        );

        string cmd;
        if (endGray == 255)
        {
            // 末尾白色，使用简短快速命令
            cmd = TjProjectorCommands.TriggerOnce;
        }
        else
        {
            // 末尾指定灰度："G {gray}\r\n"（源码：sprintf(str, "G %hhu\r\n", nGray)）
            cmd = $"{TjProjectorCommands.TriggerWithGrayPrefix}{endGray}";
        }

        return await SendAndLogAsync(
                cmd,
                ProjectorOperationType.TriggerOnce,
                $"EndGray={endGray}",
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> NextFrameAsync(CancellationToken cancellationToken = default)
    {
        EnsureClient();
        _logger.LogInformation(
            "{Tag} [Device {Device}] Trigger next fringe frame",
            LogTag,
            DeviceId
        );
        return await SendAndLogAsync(
                TjProjectorCommands.TriggerNextFrame,
                ProjectorOperationType.TriggerOnce,
                "NextFrame",
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task ConfigureFringePlaybackAsync(
        int imageCount,
        int horizontalFrameCount,
        CancellationToken cancellationToken = default
    )
    {
        return ExecuteOperationAsync(
            () => ConfigureFringePlaybackCoreAsync(
                imageCount,
                horizontalFrameCount,
                cancellationToken
            ),
            cancellationToken
        );
    }

    private async Task ConfigureFringePlaybackCoreAsync(
        int imageCount,
        int horizontalFrameCount,
        CancellationToken cancellationToken
    )
    {
        EnsureClient();
        if (imageCount is < 1 or > 128)
            throw new ArgumentOutOfRangeException(nameof(imageCount));
        if (horizontalFrameCount < 0 || horizontalFrameCount > imageCount)
            throw new ArgumentOutOfRangeException(nameof(horizontalFrameCount));

        const string NewLine = "\r\n";
        string rangeCommand =
            $"{TjProjectorCommands.SetImageRepeatPrefix}0 {imageCount - 1} 0 0{NewLine}";
        await SendCommandAndReadCoreAsync(rangeCommand, cancellationToken).ConfigureAwait(false);
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);

        string directionCommand =
            $"{TjProjectorCommands.SetFringeDirectionPrefix}{horizontalFrameCount}{NewLine}";
        await SendCommandAndReadCoreAsync(directionCommand, cancellationToken).ConfigureAwait(false);
        await Task.Delay(50, cancellationToken).ConfigureAwait(false);

        int orientationBlockCount = (imageCount + 31) / 32;
        for (int blockIndex = 0; blockIndex < orientationBlockCount; blockIndex++)
        {
            string orientationCommand = BuildFringeOrientationCommand(
                imageCount,
                horizontalFrameCount,
                blockIndex
            );
            await SendCommandAndReadCoreAsync(orientationCommand, cancellationToken)
                .ConfigureAwait(false);
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "{Tag} [Device {Device}] Fringe playback restored: images={ImageCount}, lastIndex={LastIndex}, horizontalFrames={HorizontalFrameCount}, orientationBlocks={OrientationBlockCount}",
            LogTag,
            DeviceId,
            imageCount,
            imageCount - 1,
            horizontalFrameCount,
            orientationBlockCount
        );
    }

    // ─── 通用命令 ────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<bool> SendRawCommandAsync(
        string command,
        CancellationToken cancellationToken = default
    )
    {
        EnsureClient();
        return await SendAndLogAsync(
                command,
                ProjectorOperationType.SendRawCommand,
                null,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<string?> SendRawCommandAndReadAsync(
        string command,
        CancellationToken cancellationToken = default
    )
    {
        EnsureClient();
        return await SendCommandAndReadCoreAsync(command, cancellationToken).ConfigureAwait(false);
    }

    // ─── 辅助 ───────────────────────────────────────────────────

    private void EnsureClient()
    {
        bool connected = _useHid
            ? _hidClient?.IsConnected == true
            : _tcpClient?.IsConnected == true;
        if (!connected)
            throw new InvalidOperationException(
                "Projector is not connected. Call ConnectAsync or ConnectHidAsync first."
            );
    }

    /// <summary>
    /// 发送命令并写入操作日志（fire-and-forget）
    /// </summary>
    private async Task<bool> SendAndLogAsync(
        string command,
        ProjectorOperationType operationType,
        string? parameterSummary,
        CancellationToken ct
    )
    {
        Stopwatch sw = Stopwatch.StartNew();
        bool ok;
        string? errorMessage = null;
        try
        {
            ok = await SendCommandCoreAsync(command, ct).ConfigureAwait(false);
            if (!ok)
                errorMessage = "Device reported failure (no ACK or timeout)";
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordOperationLog(
                operationType,
                command,
                parameterSummary,
                isSuccess: false,
                roundTripMs: sw.ElapsedMilliseconds,
                errorMessage: ex.Message
            );
            throw;
        }
        sw.Stop();
        RecordOperationLog(
            operationType,
            command,
            parameterSummary,
            isSuccess: ok,
            roundTripMs: sw.ElapsedMilliseconds,
            errorMessage: ok ? null : errorMessage
        );
        return ok;
    }

    /// <summary>
    /// fire-and-forget 写入投影仪操作日志，异常仅记录 Warning，不向上抛出
    /// </summary>
    private void RecordOperationLog(
        ProjectorOperationType operationType,
        string rawCommand,
        string? parameterSummary,
        bool isSuccess,
        long roundTripMs,
        string? errorMessage
    )
    {
        // 未绑定设备 ID 或无 DI 容器时跳过
        if (_projectorDeviceId == Guid.Empty || _serviceScopeFactory is null)
            return;

        Guid deviceId = _projectorDeviceId;
        _ = Task.Run(async () =>
        {
            try
            {
                using IServiceScope scope = _serviceScopeFactory.CreateScope();
                IProjectorOperationLogRepository? repo =
                    scope.ServiceProvider.GetService<IProjectorOperationLogRepository>();
                if (repo is null)
                    return;

                ProjectorOperationLog log = isSuccess
                    ? ProjectorOperationLog.Success(
                        Guid.NewGuid(),
                        deviceId,
                        operationType,
                        rawCommand,
                        parameterSummary,
                        (int)Math.Min(roundTripMs, int.MaxValue)
                    )
                    : ProjectorOperationLog.Failure(
                        Guid.NewGuid(),
                        deviceId,
                        operationType,
                        errorMessage ?? "Unknown error",
                        rawCommand,
                        parameterSummary,
                        (int)Math.Min(roundTripMs, int.MaxValue)
                    );

                await repo.InsertAsync(log).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "{Tag} [Device {Device}] Failed to write operation log (Operation={Op})",
                    LogTag,
                    DeviceId,
                    operationType
                );
            }
        });
    }

    /// <summary>向当前激活连接发送命令（不读取响应）</summary>
    private Task<bool> SendCommandCoreAsync(string command, CancellationToken ct)
    {
        return ExecuteOperationAsync(() => SendCommandCoreUnsafeAsync(command, ct), ct);
    }

    private Task<bool> SendCommandCoreUnsafeAsync(string command, CancellationToken ct)
    {
        if (_useHid)
            return _hidClient!.SendCommandAsync(command, ct);
        return _tcpClient!.SendCommandAsync(command, ct);
    }

    /// <summary>向当前激活连接发送命令并读取一行响应</summary>
    private Task<string?> SendCommandAndReadCoreAsync(string command, CancellationToken ct)
    {
        return ExecuteOperationAsync(() => SendCommandAndReadCoreUnsafeAsync(command, ct), ct);
    }

    private Task<string?> SendCommandAndReadCoreUnsafeAsync(string command, CancellationToken ct)
    {
        if (_useHid)
            return _hidClient!.SendCommandAndReadAsync(command, ct);
        return _tcpClient!.SendCommandAndReadAsync(command, ct);
    }

    /// <summary>仅读取一行响应，不发送任何命令（用于等待 Flash page 写入应答）</summary>
    private Task<string?> ReadResponseCoreAsync(CancellationToken ct)
    {
        return ExecuteOperationAsync(() => ReadResponseCoreUnsafeAsync(ct), ct);
    }

    private Task<string?> ReadResponseCoreUnsafeAsync(CancellationToken ct)
    {
        if (_useHid)
            return _hidClient!.ReadResponseAsync(ct);
        return _tcpClient!.ReadResponseAsync(ct);
    }

    private async Task<T> ExecuteOperationAsync<T>(Func<Task<T>> operation, CancellationToken ct)
    {
        if (_operationDepth.Value > 0)
            return await operation().ConfigureAwait(false);

        await _operationLock.WaitAsync(ct).ConfigureAwait(false);
        _operationDepth.Value = 1;
        try
        {
            return await operation().ConfigureAwait(false);
        }
        finally
        {
            _operationDepth.Value = 0;
            _operationLock.Release();
        }
    }

    private async Task ExecuteOperationAsync(Func<Task> operation, CancellationToken ct)
    {
        if (_operationDepth.Value > 0)
        {
            await operation().ConfigureAwait(false);
            return;
        }

        await _operationLock.WaitAsync(ct).ConfigureAwait(false);
        _operationDepth.Value = 1;
        try
        {
            await operation().ConfigureAwait(false);
        }
        finally
        {
            _operationDepth.Value = 0;
            _operationLock.Release();
        }
    }

    /// <summary>当前连接的设备标识符（用于日志）</summary>
    private string DeviceId =>
        _useHid
            ? $"HID 0x{_currentHidVid:X4}/0x{_currentHidPid:X4}[{_currentHidIndex}]"
            : _currentIp;

    /// <inheritdoc/>
    public void Dispose()
    {
        _tcpClient?.Dispose();
        _hidClient?.Dispose();
    }

    // ─── 高级控制（Phase 4 新增）────────────────────────────────

    /// <inheritdoc/>
    public async Task<bool> SetFlipAsync(
        ProjectorFlipMode flip,
        CancellationToken cancellationToken = default
    )
    {
        EnsureClient();
        _logger.LogInformation(
            "{Tag} [Device {Device}] Set flip mode: {Flip}",
            LogTag,
            DeviceId,
            flip
        );
        string cmd = $"{TjProjectorCommands.SetFlipPrefix}{(int)flip}";
        return await SendAndLogAsync(
                cmd,
                ProjectorOperationType.SetFlip,
                $"FlipMode={flip}",
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> SetTriggerModeAsync(
        ProjectorTriggerMode mode,
        CancellationToken cancellationToken = default
    )
    {
        EnsureClient();
        _logger.LogInformation(
            "{Tag} [Device {Device}] Set trigger mode: {Mode}",
            LogTag,
            DeviceId,
            mode
        );
        string cmd = $"{TjProjectorCommands.SetTriggerModePrefix}{(int)mode}";
        return await SendAndLogAsync(
                cmd,
                ProjectorOperationType.SetTriggerMode,
                $"TriggerMode={mode}",
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> SetBootImageAsync(
        ProjectorBootImage image,
        CancellationToken cancellationToken = default
    )
    {
        EnsureClient();
        _logger.LogInformation(
            "{Tag} [Device {Device}] Set boot image: {Image}",
            LogTag,
            DeviceId,
            image
        );
        string cmd = $"{TjProjectorCommands.SetBootImagePrefix}{(int)image}";
        return await SendAndLogAsync(
                cmd,
                ProjectorOperationType.SetBootImage,
                $"BootImage={image}",
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> SetCheckerboardPixelSizeAsync(
        int pixelSize,
        CancellationToken cancellationToken = default
    )
    {
        EnsureClient();
        _logger.LogInformation(
            "{Tag} [Device {Device}] Set checkerboard pixel size: {Size}",
            LogTag,
            DeviceId,
            pixelSize
        );
        string cmd = $"{TjProjectorCommands.SetCheckerboardPixelPrefix}{pixelSize}";
        return await SendAndLogAsync(
                cmd,
                ProjectorOperationType.SetCheckerboardPixel,
                $"PixelSize={pixelSize}",
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> SetRgbColorAsync(
        byte r,
        byte g,
        byte b,
        CancellationToken cancellationToken = default
    )
    {
        EnsureClient();

        // 将颜色分量 (0~255) 线性映射为 LED 亮度值 (0~175)
        byte lr = (byte)Math.Round(r / 255.0 * 175);
        byte lg = (byte)Math.Round(g / 255.0 * 175);
        byte lb = (byte)Math.Round(b / 255.0 * 175);

        _logger.LogInformation(
            "{Tag} [Device {Device}] Set RGB color: color=#{R:X2}{G:X2}{B:X2} -> LED brightness R={LR} G={LG} B={LB}",
            LogTag,
            DeviceId,
            r,
            g,
            b,
            lr,
            lg,
            lb
        );

        // 协议：单条 LE r g b\r\n 同时使能彩光并设置 RGB 分量亮度（0~175）
        string cmd = $"{TjProjectorCommands.SetRgbColorPrefix}{lr} {lg} {lb}";
        return await SendAndLogAsync(
                cmd,
                ProjectorOperationType.SetRgbColor,
                $"color=#{r:X2}{g:X2}{b:X2} LED={lr},{lg},{lb}",
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> SoftResetAsync(CancellationToken cancellationToken = default)
    {
        EnsureClient();
        _logger.LogInformation("{Tag} [Device {Device}] Soft reset", LogTag, DeviceId);
        return await SendAndLogAsync(
                TjProjectorCommands.SoftReset,
                ProjectorOperationType.SoftReset,
                null,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> SaveParamsAsync(CancellationToken cancellationToken = default)
    {
        EnsureClient();
        _logger.LogInformation("{Tag} [Device {Device}] Save params to flash", LogTag, DeviceId);
        return await SendAndLogAsync(
                TjProjectorCommands.SaveParams,
                ProjectorOperationType.SaveParams,
                null,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<string?> ReadRegisterAsync(
        int address,
        CancellationToken cancellationToken = default
    )
    {
        EnsureClient();
        _logger.LogInformation(
            "{Tag} [Device {Device}] Read register {Addr}",
            LogTag,
            DeviceId,
            address
        );
        string cmd = $"{TjProjectorCommands.ReadRegisterPrefix}{address}";
        return await SendCommandAndReadCoreAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> WriteRegisterAsync(
        int address,
        int value,
        CancellationToken cancellationToken = default
    )
    {
        EnsureClient();
        _logger.LogInformation(
            "{Tag} [Device {Device}] Write register {Addr}={Value}",
            LogTag,
            DeviceId,
            address,
            value
        );
        string cmd = $"{TjProjectorCommands.WriteRegisterPrefix}{address} {value}";
        return await SendAndLogAsync(
                cmd,
                ProjectorOperationType.WriteRegister,
                $"addr={address},val={value}",
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    // ─── 像素分辨率查询与 Flash 条纹下载 ──────────────────────────────────

    /// <inheritdoc/>
    public async Task<(int WidthPixels, string PixelMode)> GetPixelResolutionAsync(
        CancellationToken cancellationToken = default
    )
    {
        EnsureClient();
        _logger.LogInformation(
            "{Tag} [Device {Device}] Query pixel resolution (Fp)",
            LogTag,
            DeviceId
        );

        // 发送 Fp 指令，响应格式示例："1280 Pixel Mode"
        string? response = await SendCommandAndReadCoreAsync(
                TjProjectorCommands.ReadPixelMode,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(response))
        {
            _logger.LogWarning(
                "{Tag} [Device {Device}] Fp command returned empty response, using default 1280",
                LogTag,
                DeviceId
            );
            return (1280, "1280 Pixel Mode");
        }

        // 解析：取第一个空格前的数字
        string[] parts = response.Trim().Split(' ', 2);
        if (parts.Length > 0 && int.TryParse(parts[0], out int width) && width > 0)
        {
            _logger.LogInformation(
                "{Tag} [Device {Device}] Pixel resolution: {Width} pixels, mode: {Mode}",
                LogTag,
                DeviceId,
                width,
                response
            );
            return (width, response);
        }

        _logger.LogWarning(
            "{Tag} [Device {Device}] Failed to parse Fp response '{Resp}', using default 1280",
            LogTag,
            DeviceId,
            response
        );
        return (1280, response);
    }

    /// <inheritdoc/>
    public Task DownloadFringePatternAsync(
        byte[][] frames,
        int horizontalFrameCount,
        string horizontalPaddingPosition = "end",
        Func<int, Task>? onProgress = null,
        CancellationToken cancellationToken = default
    )
    {
        return ExecuteOperationAsync(
            () => DownloadFringePatternCoreAsync(
                frames, horizontalFrameCount, horizontalPaddingPosition, onProgress, cancellationToken
            ),
            cancellationToken
        );
    }

    private async Task DownloadFringePatternCoreAsync(
        byte[][] frames,
        int horizontalFrameCount,
        string horizontalPaddingPosition,
        Func<int, Task>? onProgress,
        CancellationToken cancellationToken
    )
    {
        EnsureClient();

        int imageCount = frames.Length;
        if (horizontalFrameCount < 0 || horizontalFrameCount > imageCount)
            throw new ArgumentOutOfRangeException(nameof(horizontalFrameCount));

        // 标准模式下每帧统一为 WidthPixels 列（1280），横条纹帧按厂商格式补 560 列白色（255）到 1280
        int frameStride = frames.Max(f => f.Length);
        int totalWrites = imageCount * frameStride;

        _logger.LogInformation(
            "{Tag} [Device {Device}] Start fringe download (standard 2-param FW mode): images={Count}, frameStride={Stride}, totalWrites={Total}",
            LogTag,
            DeviceId,
            imageCount,
            frameStride,
            totalWrites
        );

        const string NewLine = "\r\n";
        // 厂商 V1.2 文档建议约 40ms；过短会出现白条、灰阶异常或末尾数据丢失。
        const int WriteDelayMs = 40;
        const int MaxEraseRetries = 5;

        // 1. 开灯并设置下载图像幅数。下载阶段的 MB 参数是实际分配幅数；
        //    MA 的“保存条纹数量”才使用末帧索引语义。混用会导致最后一幅空间不足。
        await SendCommandCoreAsync(TjProjectorCommands.LedOn + NewLine, cancellationToken)
            .ConfigureAwait(false);
        await Task.Delay(50, cancellationToken).ConfigureAwait(false);

        string mbCmd = $"{TjProjectorCommands.SetImageCountPrefix}{imageCount}{NewLine}";
        _logger.LogDebug(
            "{Tag} [Device {Device}] Set image count: cmd={Cmd}",
            LogTag,
            DeviceId,
            mbCmd.TrimEnd('\r', '\n')
        );
        await SendCommandAndReadCoreAsync(mbCmd, cancellationToken).ConfigureAwait(false);
        await Task.Delay(500, cancellationToken).ConfigureAwait(false);

        // MA 参数1是“额外重复次数”，0 表示每幅只显示一次；
        // 参数2是末帧索引而不是图片数量，下载 N 幅时必须写 N-1。
        string maCmd =
            $"{TjProjectorCommands.SetImageRepeatPrefix}0 {imageCount - 1} 0 0{NewLine}";
        _logger.LogDebug(
            "{Tag} [Device {Device}] Set image range: cmd={Cmd}, images={Count}, lastIndex={LastIndex}",
            LogTag,
            DeviceId,
            maCmd.TrimEnd('\r', '\n'),
            imageCount,
            imageCount - 1
        );
        await SendCommandAndReadCoreAsync(maCmd, cancellationToken).ConfigureAwait(false);
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);

        // 2. MD 参数表示序列开头连续的横条纹数量，其余帧全部为竖条纹。
        string mdCmd =
            $"{TjProjectorCommands.SetFringeDirectionPrefix}{horizontalFrameCount}{NewLine}";
        _logger.LogDebug(
            "{Tag} [Device {Device}] MD horizontal frame count: cmd={Cmd}",
            LogTag,
            DeviceId,
            mdCmd.TrimEnd('\r', '\n')
        );
        await SendCommandAndReadCoreAsync(mdCmd, cancellationToken).ConfigureAwait(false);
        await Task.Delay(50, cancellationToken).ConfigureAwait(false);

        // MF 才是固件逐帧解释横/竖条纹数据长度的方向位图。每个块覆盖 32 帧，
        // bit=1 表示横条纹，bit=0 表示竖条纹。当前布局是前 H 帧横、其余帧竖，
        // 不能继续使用旧版 1-2-1-2 横竖交替位图。
        int orientationBlockCount = (imageCount + 31) / 32;
        for (int blockIndex = 0; blockIndex < orientationBlockCount; blockIndex++)
        {
            string mfCmd = BuildFringeOrientationCommand(
                imageCount,
                horizontalFrameCount,
                blockIndex
            );
            _logger.LogInformation(
                "{Tag} [Device {Device}] Set fringe orientation: cmd={Cmd}",
                LogTag,
                DeviceId,
                mfCmd.TrimEnd('\r', '\n')
            );
            await SendCommandAndReadCoreAsync(mfCmd, cancellationToken).ConfigureAwait(false);
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        // 方向配置单独持久化；不要在下载时用 MA 覆盖 B2 单帧触发参数。
        await SendCommandAndReadCoreAsync(TjProjectorCommands.SaveFringeParams + NewLine, cancellationToken)
            .ConfigureAwait(false);
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);

        // 3. 擦除 Flash。协议只要求一次成功的 FE/F0；重复擦除会增加耗时和 Flash 风险。
        async Task<bool> SendFeAndWaitAsync(int attemptNum)
        {
            for (int attempt = 0; attempt < MaxEraseRetries; attempt++)
            {
                string? eraseReply = await SendCommandAndReadCoreAsync(
                        TjProjectorCommands.EraseFlash + NewLine,
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                _logger.LogInformation(
                    "{Tag} [Device {Device}] FE erase (attempt {AttemptNum}-{Attempt}): reply={Reply}",
                    LogTag,
                    DeviceId,
                    attemptNum,
                    attempt + 1,
                    eraseReply
                );

                if (
                    eraseReply != null
                    && eraseReply
                        .Trim()
                        .StartsWith(
                            TjProjectorCommands.FlashEraseOk,
                            StringComparison.OrdinalIgnoreCase
                        )
                )
                {
                    return true;
                }

                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }

            return false;
        }

        if (!await SendFeAndWaitAsync(1).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"[{LogTag}] FE erase failed after {MaxEraseRetries} attempts."
            );
        }

        await Task.Delay(5000, cancellationToken).ConfigureAwait(false);

        // 4. 循环写数据（标准模式：FW{globalCol} {gray}，全局连续列索引从0开始）。
        //    标准两参数 FW 格式（参考官方说明书）：
        //      FW{全局列号} {灰度值}\r\n；
        //      全局列号从0开始连续计数，每帧固定 frameStride(1280) 列；
        //      横条纹帧：前 HeightPixels(720) 列为条纹数据，后 frameStride-HeightPixels(560) 列填255（白色补齐）；
        //      竖条纹帧：全部 frameStride(1280) 列为条纹数据；
        //      Flash 采取 page 编程模式，每写入256个数据后等待page写入应答。
        //    例：2幅(横-竖，1280*720，周期2数量1相移1)：
        //      帧0(H): 全局列0~1279（前720列条纹数据 + 后560列填255）
        //      帧1(V): 全局列1280~2559（1280列条纹数据）
        int lastReportedProgress = 0;
        int writtenCount = 0;
        if (onProgress != null)
            await onProgress(0).ConfigureAwait(false);

        int globalCol = 0; // 厂商下载 Demo 与页边界均采用 0-based 列索引

        for (int frameIdx = 0; frameIdx < imageCount; frameIdx++)
        {
            byte[] frame = frames[frameIdx];
            bool isHorizontal = frameIdx < horizontalFrameCount;

            // 横条纹帧实际条纹数据长度为 720，地址空间为 1280，需按厂商格式补 560 列白色。
            // horizontalPaddingPosition 控制填充位置：end=后置（右侧，默认），start=前置（左侧）。
            byte[] frameData;
            if (frame.Length < frameStride)
            {
                frameData = new byte[frameStride];
                // 腾聚横条纹文件格式及原厂横条纹样例均使用 0xFF 补齐到 1280 字节。
                Array.Fill(frameData, byte.MaxValue);
                bool padAtStart = horizontalPaddingPosition
                    .Equals("start", StringComparison.OrdinalIgnoreCase);
                if (padAtStart)
                {
                    // 前置填充：条纹数据放在右侧
                    Array.Copy(frame, 0, frameData, frameStride - frame.Length, frame.Length);
                }
                else
                {
                    // 后置填充：条纹数据放在左侧（默认）
                    Array.Copy(frame, frameData, frame.Length);
                }
            }
            else
            {
                frameData = frame;
            }

            _logger.LogInformation(
                "{Tag} [Device {Device}] Writing frame {Frame}: orientation={Orient}, dataLen={DataLen}, stride={Stride}, globalCol range=[{Start},{End}]",
                LogTag,
                DeviceId,
                frameIdx,
                isHorizontal ? "H(横)" : "V(竖)",
                frame.Length,
                frameStride,
                globalCol,
                globalCol + frameStride - 1
            );

            for (int colIdx = 0; colIdx < frameStride; colIdx++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 标准模式两参数格式：FW{globalCol} {gray}\r\n
                string fwCmd =
                    $"{TjProjectorCommands.WriteFlashPixelPrefix}{globalCol} {frameData[colIdx]}{NewLine}";
                await SendCommandCoreAsync(fwCmd, cancellationToken).ConfigureAwait(false);
                await Task.Delay(WriteDelayMs, cancellationToken).ConfigureAwait(false);

                // 每写入256个数据后必须收到 page 写入应答；不能把超时当作成功，
                // 否则常表现为最后一幅只有前半幅或缺少整块。
                if ((globalCol + 1) % 256 == 0)
                {
                    string? pageReply = await ReadResponseCoreAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(pageReply))
                    {
                        throw new IOException(
                            $"[{LogTag}] Flash page ACK timeout at column {globalCol}."
                        );
                    }
                }

                globalCol++;
                writtenCount++;

                int currentProgress = (int)(writtenCount * 100L / totalWrites);
                if (currentProgress > lastReportedProgress)
                {
                    lastReportedProgress = currentProgress;
                    if (onProgress != null)
                        await onProgress(currentProgress).ConfigureAwait(false);
                }
            }
        }

        // 所有 FW 数据写完后，等待 500ms 确保最后一批数据被固件处理
        await Task.Delay(1000, cancellationToken).ConfigureAwait(false);

        // 6. 软复位（X\r\n）：不断电情况下让固件重新加载 Flash 中的条纹数据和方向配置
        _logger.LogInformation(
            "{Tag} [Device {Device}] Sending soft-reset (X) to activate new fringe patterns...",
            LogTag,
            DeviceId
        );
        await SendCommandAndReadCoreAsync(
                TjProjectorCommands.SoftReset + NewLine,
                cancellationToken
            )
            .ConfigureAwait(false);
        await Task.Delay(2000, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "{Tag} [Device {Device}] Fringe download complete: {Total} values written (standard 2-param FW mode, globalCol 0~{End})",
            LogTag,
            DeviceId,
            totalWrites,
            globalCol - 1
        );
    }

    /// <summary>
    /// 构建条纹方向位图配置命令（MF）。
    /// </summary>
    /// <param name="imageCount">图像总幅数</param>
    /// <param name="horizontalFrameCount">序列开头连续的横条纹幅数</param>
    /// <param name="blockIndex">MF 块索引，新型光机使用 0，旧型号使用 1</param>
    /// <returns>完整 MF 命令字符串（含 \r\n 结尾）</returns>
    internal static string BuildFringeOrientationCommand(
        int imageCount,
        int horizontalFrameCount,
        int blockIndex)
    {
        if (blockIndex is < 0 or > 3)
            throw new ArgumentOutOfRangeException(nameof(blockIndex));
        if (imageCount is < 0 or > 128)
            throw new ArgumentOutOfRangeException(nameof(imageCount));
        if (horizontalFrameCount < 0 || horizontalFrameCount > imageCount)
            throw new ArgumentOutOfRangeException(nameof(horizontalFrameCount));

        byte[] bits = BuildFringeOrientationBits(imageCount, horizontalFrameCount);
        int offset = blockIndex * 4;
        return $"{TjProjectorCommands.SetFringeOrientationBitmapPrefix}{blockIndex} {bits[offset]} {bits[offset + 1]} {bits[offset + 2]} {bits[offset + 3]}\r\n";
    }

    private static byte[] BuildFringeOrientationBits(
        int imageCount,
        int horizontalFrameCount)
    {
        const int maxImages = 128;
        byte[] bits = new byte[16];
        int usableCount = Math.Clamp(imageCount, 0, maxImages);
        int horizontalCount = Math.Clamp(horizontalFrameCount, 0, usableCount);

        for (int frame = 0; frame < horizontalCount; frame++)
        {
            int byteIndex = frame / 8;
            int bitPosition = frame % 8;
            bits[byteIndex] = (byte)(bits[byteIndex] | (1 << bitPosition));
        }

        return bits;
    }
}
