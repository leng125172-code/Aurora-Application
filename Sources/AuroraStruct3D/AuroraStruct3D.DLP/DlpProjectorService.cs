using AuroraStruct3D.DLP.Protocol;
using AuroraStruct3D.Projectors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace AuroraStruct3D.DLP;

/// <summary>
/// 腾聚（TJ）结构光投影机控制服务实现。
/// 支持 TCP/IP 和 USB HID 两种接口，ASCII 命令协议。
/// </summary>
public class DlpProjectorService : IDlpProjectorService, IDisposable
{
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
            _logger.LogDebug("投影机 {Ip}:{Port} 已连接，跳过重复连接", ip, port);
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
        int vendorId = 0x0E6A,
        int productId = 0x0317,
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
                "投影机 HID 0x{Vid:X4}/0x{Pid:X4}[{Idx}] 已连接，跳过重复连接",
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
            _useHid ? "投影机 HID 0x{Vid:X4}/0x{Pid:X4} 已断开连接" : "投影机 {Ip} 已断开连接",
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
        _logger.LogInformation("[投影机 {Device}] 开灯", DeviceId);
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
        _logger.LogInformation("[投影机 {Device}] 关灯", DeviceId);
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
            throw new ArgumentOutOfRangeException(nameof(light), "亮度值必须在 10~200 范围内");
        }

        _logger.LogInformation("[投影机 {Device}] 设置亮度 {Light}", DeviceId, light);

        // 亮度 > 175 时需先发送高亮使能命令（源码：TJSTPrjSetLight）
        if (light > 175)
        {
            bool hlOk = await SendAndLogAsync(
                    TjProjectorCommands.HighLightEnable,
                    ProjectorOperationType.SetLight,
                    "高亮使能",
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (!hlOk)
            {
                return false;
            }
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        string cmd =
            $"{TjProjectorCommands.SetLightPrefix}{light}{TjProjectorCommands.CommandSuffix}";
        bool ok = await SendAndLogAsync(
                cmd,
                ProjectorOperationType.SetLight,
                $"亮度={light}",
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
        _logger.LogInformation("[投影机 {Device}] 设置显示模式: {Mode}", DeviceId, mode);

        // 模式命令：'S' + ('0' + mode) + '\r' + '\n'（源码：TJSTPrjSetMode）
        char modeChar = (char)(TjProjectorCommands.SetModeOffsetBase + (byte)mode);
        string cmd = $"S{modeChar}\r\n";
        return await SendAndLogAsync(
                cmd,
                ProjectorOperationType.SetDisplayMode,
                $"模式={mode}",
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
        _logger.LogInformation("[投影机 {Device}] 设置颜色: {Color}", DeviceId, color);

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
                $"颜色={color}",
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    // ─── 条纹投影触发 ─────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<bool> TriggerOnceAsync(CancellationToken cancellationToken = default)
    {
        EnsureClient();
        _logger.LogInformation("[投影机 {Device}] 触发条纹投影（白色末尾）", DeviceId);
        // nGray == 255 时使用 "T\r\n" 快速命令（源码：TJSTPrjTriggerOnce）
        return await SendAndLogAsync(
                TjProjectorCommands.TriggerOnce,
                ProjectorOperationType.TriggerOnce,
                "末尾灰度=255(白)",
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
            "[投影机 {Device}] 触发条纹投影（末尾灰度={Gray}）",
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
            cmd =
                $"{TjProjectorCommands.TriggerWithGrayPrefix}{endGray}{TjProjectorCommands.CommandSuffix}";
        }

        return await SendAndLogAsync(
                cmd,
                ProjectorOperationType.TriggerOnce,
                $"末尾灰度={endGray}",
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
                "投影机未连接，请先调用 ConnectAsync 或 ConnectHidAsync"
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
                errorMessage = "设备返回失败（无 ACK 或超时）";
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
    /// fire-and-forget 写入投影机操作日志，异常仅记录 Warning，不向上抛出
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
                IProjectorOperationLogRepository? repo = scope
                    .ServiceProvider.GetService<IProjectorOperationLogRepository>();
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
                        errorMessage ?? "未知错误",
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
                    "[投影机 {Device}] 写入操作日志失败（操作={Op}）",
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
}
