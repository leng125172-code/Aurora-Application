using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace AuroraStruct3D.Projectors.Protocol;

/// <summary>
/// 腾聚（TJ）结构光投影机 TCP 通信客户端。
/// 封装底层 TCP 连接、ASCII 命令发送和响应读取，线程安全（SemaphoreSlim 互斥）。
/// 支持 linux-arm64 和 Windows 平台，无需原生 DLL。
/// </summary>
internal sealed class TjProjectorTcpClient : IDisposable
{
    private const string LogTag = "[Projector]";

    private readonly string _ip;
    private readonly int _port;
    private readonly ILogger _logger;

    private readonly SemaphoreSlim _lock = new(1, 1);

    private TcpClient? _tcpClient;
    private NetworkStream? _stream;

    /// <summary>连接超时时间（毫秒）</summary>
    private const int ConnectTimeoutMs = 5000;

    /// <summary>命令读取超时时间（毫秒）</summary>
    private const int ReadTimeoutMs = 1000;

    /// <summary>是否已连接</summary>
    public bool IsConnected => _tcpClient?.Connected == true && _stream != null;

    public TjProjectorTcpClient(string ip, int port, ILogger logger)
    {
        _ip = ip;
        _port = port;
        _logger = logger;
    }

    /// <summary>
    /// 建立 TCP 连接（超时 5 秒）
    /// </summary>
    /// <exception cref="InvalidOperationException">连接超时或失败时抛出</exception>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsConnected)
            {
                return;
            }

            _tcpClient?.Dispose();
            _tcpClient = new TcpClient();

            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken
            );
            cts.CancelAfter(ConnectTimeoutMs);

            try
            {
                await _tcpClient.ConnectAsync(_ip, _port, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    $"{LogTag} Connecting to projector {_ip}:{_port} timed out ({ConnectTimeoutMs} ms)."
                );
            }

            _stream = _tcpClient.GetStream();
            _stream.ReadTimeout = ReadTimeoutMs;
            _stream.WriteTimeout = 500;

            _logger.LogInformation(
                "{Tag} Projector TCP connected: {Ip}:{Port}",
                LogTag,
                _ip,
                _port
            );
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 断开 TCP 连接
    /// </summary>
    public async Task DisconnectAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            CloseConnection();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 发送 ASCII 命令，不等待响应（用于 LED/触发/模式设置等单向指令）
    /// </summary>
    /// <param name="command">ASCII 命令字符串（含 \r\n）</param>
    public async Task<bool> SendCommandAsync(
        string command,
        CancellationToken cancellationToken = default
    )
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureConnected();
            byte[] data = Encoding.ASCII.GetBytes(command);
            await _stream!.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug(
                "{Tag} [IP {Ip}] TX command: {Cmd}",
                LogTag,
                _ip,
                command.TrimEnd('\r', '\n')
            );
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "{Tag} [IP {Ip}] Send command failed: {Cmd}",
                LogTag,
                _ip,
                command.TrimEnd('\r', '\n')
            );
            CloseConnection();
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 发送 ASCII 命令并读取响应行
    /// </summary>
    /// <param name="command">ASCII 命令字符串（含 \r\n）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>响应文本（去除 \r\n），超时或错误时返回 null</returns>
    public async Task<string?> SendCommandAndReadAsync(
        string command,
        CancellationToken cancellationToken = default
    )
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureConnected();
            byte[] data = Encoding.ASCII.GetBytes(command);
            await _stream!.WriteAsync(data, cancellationToken).ConfigureAwait(false);

            // 延迟等待设备处理（对应 C++ 源码中的 Sleep(50)）
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);

            byte[] buffer = new byte[128];
            int bytesRead = 0;
            try
            {
                bytesRead = await _stream
                    .ReadAsync(buffer, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (IOException)
            {
                // 读取超时，返回 null
                return null;
            }

            if (bytesRead <= 0)
            {
                return null;
            }

            string response = Encoding.ASCII.GetString(buffer, 0, bytesRead).TrimEnd('\r', '\n');
            _logger.LogDebug(
                "{Tag} [IP {Ip}] Command {Cmd} -> Response: {Resp}",
                LogTag,
                _ip,
                command.TrimEnd('\r', '\n'),
                response
            );
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "{Tag} [IP {Ip}] Send command and read response failed: {Cmd}",
                LogTag,
                _ip,
                command.TrimEnd('\r', '\n')
            );
            CloseConnection();
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 仅读取一行响应，不发送任何命令（用于等待 Flash page 写入完成的应答）。
    /// 超时或未连接时返回 null。
    /// </summary>
    public async Task<string?> ReadResponseAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureConnected();
            byte[] buffer = new byte[128];
            int bytesRead = 0;
            try
            {
                bytesRead = await _stream!
                    .ReadAsync(buffer, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (IOException)
            {
                return null;
            }

            if (bytesRead <= 0)
                return null;

            string response = Encoding.ASCII.GetString(buffer, 0, bytesRead).TrimEnd('\r', '\n');
            _logger.LogDebug("{Tag} [IP {Ip}] Page-write ACK: {Resp}", LogTag, _ip, response);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Tag} [IP {Ip}] ReadResponseAsync failed", LogTag, _ip);
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException(
                $"{LogTag} Projector {_ip}:{_port} is not connected. Call ConnectAsync first."
            );
        }
    }

    private void CloseConnection()
    {
        try
        {
            _stream?.Dispose();
        }
        catch
        {
            // 忽略关闭异常
        }

        try
        {
            _tcpClient?.Dispose();
        }
        catch
        {
            // 忽略关闭异常
        }

        _stream = null;
        _tcpClient = null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        CloseConnection();
        _lock.Dispose();
    }
}
