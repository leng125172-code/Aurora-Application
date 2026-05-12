using AuroraStruct3D.DLP.Protocol;
using Microsoft.Extensions.Logging;

namespace AuroraStruct3D.DLP;

/// <summary>
/// 腾聚（TJ）结构光投影机 TCP 控制服务实现。
/// 通过 ASCII 文本协议（TCP 端口 1234）控制投影机，完全跨平台，支持 linux-arm64 和 Windows。
/// </summary>
/// <remarks>
/// 协议参考：TJProjector.cpp / TJSTProjectorApi.cpp（腾聚官方 C++ SDK 源码）
/// 通信方式：TCP 长连接，ASCII 命令以 \r\n 结尾，延迟 50ms 等待设备处理。
///
/// 使用示例：
///   await service.ConnectAsync("192.168.100.100");
///   await service.LedOnAsync();
///   await service.TriggerOnceAsync();
///   await service.LedOffAsync();
///   await service.DisconnectAsync();
/// </remarks>
public class DlpProjectorService : IDlpProjectorService, IDisposable
{
    private readonly ILogger<DlpProjectorService> _logger;

    private TjProjectorTcpClient? _client;
    private string _currentIp = string.Empty;
    private int _currentPort = TjProjectorCommands.TcpPort;

    public DlpProjectorService(ILogger<DlpProjectorService> logger)
    {
        _logger = logger;
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
        if (_client != null && _currentIp == ip && _currentPort == port && _client.IsConnected)
        {
            _logger.LogDebug("投影机 {Ip}:{Port} 已连接，跳过重复连接", ip, port);
            return;
        }

        // 关闭旧连接
        if (_client != null)
        {
            await _client.DisconnectAsync().ConfigureAwait(false);
            _client.Dispose();
        }

        _currentIp = ip;
        _currentPort = port;
        _client = new TjProjectorTcpClient(ip, port, _logger);
        await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DisconnectAsync()
    {
        if (_client != null)
        {
            await _client.DisconnectAsync().ConfigureAwait(false);
            _logger.LogInformation("投影机 {Ip} 已断开连接", _currentIp);
        }
    }

    /// <inheritdoc/>
    public async Task<DlpProjectorStatus> GetStatusAsync(
        CancellationToken cancellationToken = default
    )
    {
        bool connected = _client?.IsConnected == true;
        string? version = null;

        if (connected)
        {
            version = await GetFirmwareVersionAsync(cancellationToken).ConfigureAwait(false);
        }

        return new DlpProjectorStatus
        {
            IpAddress = _currentIp,
            Port = _currentPort,
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
        return await _client!
            .SendCommandAndReadAsync(TjProjectorCommands.ReadVersion, cancellationToken)
            .ConfigureAwait(false);
    }

    // ─── LED 控制 ──────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<bool> LedOnAsync(CancellationToken cancellationToken = default)
    {
        EnsureClient();
        _logger.LogInformation("[投影机 {Ip}] 开灯", _currentIp);
        bool ok = await _client!
            .SendCommandAsync(TjProjectorCommands.LedOn, cancellationToken)
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
        _logger.LogInformation("[投影机 {Ip}] 关灯", _currentIp);
        bool ok = await _client!
            .SendCommandAsync(TjProjectorCommands.LedOff, cancellationToken)
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

        _logger.LogInformation("[投影机 {Ip}] 设置亮度 {Light}", _currentIp, light);

        // 亮度 > 175 时需先发送高亮使能命令（源码：TJSTPrjSetLight）
        if (light > 175)
        {
            bool hlOk = await _client!
                .SendCommandAsync(TjProjectorCommands.HighLightEnable, cancellationToken)
                .ConfigureAwait(false);
            if (!hlOk)
            {
                return false;
            }
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        string cmd =
            $"{TjProjectorCommands.SetLightPrefix}{light}{TjProjectorCommands.CommandSuffix}";
        bool ok = await _client!.SendCommandAsync(cmd, cancellationToken).ConfigureAwait(false);
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
        _logger.LogInformation("[投影机 {Ip}] 设置显示模式: {Mode}", _currentIp, mode);

        // 模式命令：'S' + ('0' + mode) + '\r' + '\n'（源码：TJSTPrjSetMode）
        char modeChar = (char)(TjProjectorCommands.SetModeOffsetBase + (byte)mode);
        string cmd = $"S{modeChar}\r\n";
        return await _client!.SendCommandAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> SetColorAsync(
        ProjectorColor color,
        CancellationToken cancellationToken = default
    )
    {
        EnsureClient();
        _logger.LogInformation("[投影机 {Ip}] 设置颜色: {Color}", _currentIp, color);

        string cmd = color switch
        {
            ProjectorColor.Red => TjProjectorCommands.ColorRed,
            ProjectorColor.Green => TjProjectorCommands.ColorGreen,
            ProjectorColor.Blue => TjProjectorCommands.ColorBlue,
            ProjectorColor.White => TjProjectorCommands.ColorWhite,
            _ => TjProjectorCommands.ColorWhite,
        };
        return await _client!.SendCommandAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    // ─── 条纹投影触发 ─────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<bool> TriggerOnceAsync(CancellationToken cancellationToken = default)
    {
        EnsureClient();
        _logger.LogInformation("[投影机 {Ip}] 触发条纹投影（白色末尾）", _currentIp);
        // nGray == 255 时使用 "T\r\n" 快速命令（源码：TJSTPrjTriggerOnce）
        return await _client!
            .SendCommandAsync(TjProjectorCommands.TriggerOnce, cancellationToken)
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
            "[投影机 {Ip}] 触发条纹投影（末尾灰度={Gray}）",
            _currentIp,
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

        return await _client!.SendCommandAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    // ─── 通用命令 ────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<bool> SendRawCommandAsync(
        string command,
        CancellationToken cancellationToken = default
    )
    {
        EnsureClient();
        return await _client!.SendCommandAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<string?> SendRawCommandAndReadAsync(
        string command,
        CancellationToken cancellationToken = default
    )
    {
        EnsureClient();
        return await _client!
            .SendCommandAndReadAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    // ─── 辅助 ───────────────────────────────────────────────────

    private void EnsureClient()
    {
        if (_client == null || !_client.IsConnected)
        {
            throw new InvalidOperationException("投影机未连接，请先调用 ConnectAsync");
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _client?.Dispose();
    }
}
