using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace AuroraStruct3D.DLP.Protocol;

/// <summary>
/// 腾聚（TJ）结构光投影机 TCP 通信客户端。
/// 封装底层 TCP 连接、ASCII 命令发送和响应读取，线程安全（SemaphoreSlim 互斥）。
/// 支持 linux-arm64 和 Windows 平台，无需原生 DLL。
/// </summary>
internal sealed class TjProjectorTcpClient : IDisposable
{
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
                    $"连接投影机 {_ip}:{_port} 超时（{ConnectTimeoutMs}ms）"
                );
            }

            _stream = _tcpClient.GetStream();
            _stream.ReadTimeout = ReadTimeoutMs;
            _stream.WriteTimeout = 500;

            _logger.LogInformation("投影机 TCP 连接成功：{Ip}:{Port}", _ip, _port);
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
            _logger.LogDebug("[投影机 {Ip}] 发送命令: {Cmd}", _ip, command.TrimEnd('\r', '\n'));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[投影机 {Ip}] 发送命令失败: {Cmd}",
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
                "[投影机 {Ip}] 命令 {Cmd} → 响应: {Resp}",
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
                "[投影机 {Ip}] 发送命令并读取响应失败: {Cmd}",
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

    private void EnsureConnected()
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException(
                $"投影机 {_ip}:{_port} 未连接，请先调用 ConnectAsync"
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
