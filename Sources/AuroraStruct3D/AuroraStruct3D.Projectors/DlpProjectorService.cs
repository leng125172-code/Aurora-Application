using System.Diagnostics;
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
    public async Task ConnectAsync(
        string ip,
        int port,
        CancellationToken cancellationToken = default
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
    public async Task ConnectHidAsync(
        int vendorId = 0x0483,
        int productId = 0x5750,
        int deviceIndex = 0,
        CancellationToken cancellationToken = default
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
    public async Task DisconnectAsync()
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

        if (light is < 10 or > 200)
        {
            throw new ArgumentOutOfRangeException(
                nameof(light),
                "Brightness must be in range 10~200"
            );
        }

        _logger.LogInformation(
            "{Tag} [Device {Device}] Set brightness to {Light}",
            LogTag,
            DeviceId,
            light
        );

        // 亮度 > 175 时需先发送高亮使能命令（源码：TJSTPrjSetLight）
        if (light > 175)
        {
            bool hlOk = await SendAndLogAsync(
                    TjProjectorCommands.HighLightEnable,
                    ProjectorOperationType.SetLight,
                    "Enable highlight",
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (!hlOk)
            {
                return false;
            }
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

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
        if (_useHid)
            return _hidClient!.SendCommandAsync(command, ct);
        return _tcpClient!.SendCommandAsync(command, ct);
    }

    /// <summary>向当前激活连接发送命令并读取一行响应</summary>
    private Task<string?> SendCommandAndReadCoreAsync(string command, CancellationToken ct)
    {
        if (_useHid)
            return _hidClient!.SendCommandAndReadAsync(command, ct);
        return _tcpClient!.SendCommandAndReadAsync(command, ct);
    }

    /// <summary>仅读取一行响应，不发送任何命令（用于等待 Flash page 写入应答）</summary>
    private Task<string?> ReadResponseCoreAsync(CancellationToken ct)
    {
        if (_useHid)
            return _hidClient!.ReadResponseAsync(ct);
        return _tcpClient!.ReadResponseAsync(ct);
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
    public async Task DownloadFringePatternAsync(
        int imageCount,
        byte[] columnGrayValues,
        bool isHorizontal,
        Func<int, Task>? onProgress = null,
        CancellationToken cancellationToken = default
    )
    {
        EnsureClient();

        // 新协议采用 MF 位图按帧配置横/竖方向，isHorizontal 参数保留仅为兼容旧调用签名。
        _ = isHorizontal;

        int widthPixels = columnGrayValues.Length / imageCount;
        int totalWrites = columnGrayValues.Length;

        _logger.LogInformation(
            "{Tag} [Device {Device}] Start fringe download: images={Count}, columns={Width}, total={Total}",
            LogTag,
            DeviceId,
            imageCount,
            widthPixels,
            totalWrites
        );

        // 1. 开灯（LN）。条纹下载只写 MB/MF/FE/FW，不再把 S1-S7 内置图案误当成条纹播放模式。
        await SendCommandCoreAsync(TjProjectorCommands.LedOn, cancellationToken)
            .ConfigureAwait(false);
        await Task.Delay(50, cancellationToken).ConfigureAwait(false);

        // 2. 写入总图像幅数（MB N）
        string mbCmd = $"{TjProjectorCommands.SetImageCountPrefix}{imageCount}";
        await SendCommandCoreAsync(mbCmd, cancellationToken).ConfigureAwait(false);
        await Task.Delay(50, cancellationToken).ConfigureAwait(false);

        // 3. 使用 MF 位图配置每一幅条纹的横竖方向：固定 1-2-1-2...（横-竖交替）
        // bit=1 表示横条纹，bit=0 表示竖条纹。第 0 幅起始为横条纹。
        byte[] orientationBits = BuildAlternatingFringeOrientationBits(imageCount);
        for (int block = 0; block < 4; block++)
        {
            int offset = block * 4;
            string mfCmd =
                $"{TjProjectorCommands.SetFringeOrientationBitmapPrefix}{block}"
                + $" {orientationBits[offset + 0]}"
                + $" {orientationBits[offset + 1]}"
                + $" {orientationBits[offset + 2]}"
                + $" {orientationBits[offset + 3]}";

            await SendCommandCoreAsync(mfCmd, cancellationToken).ConfigureAwait(false);
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        // 4. 擦除 Flash（FE），等待 F0 成功应答；若 F1 则重试（最多 5 次）
        const int MaxEraseRetries = 5;
        bool eraseOk = false;
        for (int attempt = 0; attempt < MaxEraseRetries; attempt++)
        {
            string? eraseReply = await SendCommandAndReadCoreAsync(
                    TjProjectorCommands.EraseFlash,
                    cancellationToken
                )
                .ConfigureAwait(false);

            _logger.LogInformation(
                "{Tag} [Device {Device}] FE erase attempt {Attempt}: reply={Reply}",
                LogTag,
                DeviceId,
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
                eraseOk = true;
                break;
            }

            // F1 或超时：等待后重试
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        if (!eraseOk)
        {
            throw new InvalidOperationException(
                $"[{LogTag}] Flash erase failed after {MaxEraseRetries} attempts. Device may be busy or disconnected."
            );
        }

        // 5. 循环写列数据（FW<index> <gray>），每条命令间隔 25ms
        const int PageSize = 256;
        int lastReportedProgress = 0;
        if (onProgress != null)
            await onProgress(0).ConfigureAwait(false);

        for (int idx = 0; idx < totalWrites; idx++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string fwCmd =
                $"{TjProjectorCommands.WriteFlashPixelPrefix}{idx} {columnGrayValues[idx]}";

            bool isPageBoundary = (idx + 1) % PageSize == 0;
            bool isLastWrite = idx == totalWrites - 1;

            if (isPageBoundary || isLastWrite)
            {
                // 到达 page 边界或最后一条：发送后等待光机 page 写入应答
                await SendCommandCoreAsync(fwCmd, cancellationToken).ConfigureAwait(false);
                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
                await ReadResponseCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // 普通写入：发送后等待 25ms 再发下一条
                await SendCommandCoreAsync(fwCmd, cancellationToken).ConfigureAwait(false);
                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            }

            // 更新进度（以 1% 为步进避免过频回调）
            int currentProgress = (int)((idx + 1) * 100L / totalWrites);
            if (currentProgress > lastReportedProgress)
            {
                lastReportedProgress = currentProgress;
                if (onProgress != null)
                    await onProgress(currentProgress).ConfigureAwait(false);
            }
        }

        _logger.LogInformation(
            "{Tag} [Device {Device}] Fringe download complete: {Total} columns written",
            LogTag,
            DeviceId,
            totalWrites
        );
    }

    private static byte[] BuildAlternatingFringeOrientationBits(int imageCount)
    {
        const int maxImages = 128;
        byte[] bits = new byte[16];
        int usableCount = Math.Clamp(imageCount, 0, maxImages);

        for (int frame = 0; frame < usableCount; frame++)
        {
            bool isHorizontalFrame = frame % 2 == 0;
            if (!isHorizontalFrame)
            {
                continue;
            }

            int byteIndex = frame / 8;
            int bitIndex = frame % 8;
            bits[byteIndex] = (byte)(bits[byteIndex] | (1 << bitIndex));
        }

        return bits;
    }
}
