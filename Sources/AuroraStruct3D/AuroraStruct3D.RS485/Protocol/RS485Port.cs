using System.IO.Ports;
using Microsoft.Extensions.Logging;

namespace AuroraStruct3D.RS485.Protocol;

/// <summary>
/// 基于 System.IO.Ports.SerialPort 的 RS485 串口通信实现。
/// 内置互斥锁，保证同一时刻只有一个请求占用总线（适用于半双工RS485多从机场景）。
/// </summary>
public sealed class RS485Port : IRS485Port
{
    private const string LogTag = "[Serial port]";

    private readonly SerialPort _serialPort;
    private readonly ILogger<RS485Port> _logger;

    /// <summary>总线互斥信号量，保证串口半双工访问安全</summary>
    private readonly SemaphoreSlim _busSemaphore = new(1, 1);

    /// <summary>是否已释放资源</summary>
    private bool _disposed;

    /// <inheritdoc/>
    public string PortName => _serialPort.PortName;

    /// <inheritdoc/>
    public bool IsOpen => _serialPort.IsOpen;

    /// <summary>
    /// 初始化RS485串口实例
    /// </summary>
    /// <param name="portName">串口设备路径（如 /dev/ttyS6 或 COM1）</param>
    /// <param name="baudRate">波特率，默认115200</param>
    /// <param name="parity">校验位，默认无校验</param>
    /// <param name="dataBits">数据位，默认8位</param>
    /// <param name="stopBits">停止位，默认1位</param>
    /// <param name="logger">日志记录器</param>
    public RS485Port(
        string portName,
        int baudRate,
        ILogger<RS485Port> logger,
        Parity parity = Parity.None,
        int dataBits = 8,
        StopBits stopBits = StopBits.One
    )
    {
        _logger = logger;
        _serialPort = new SerialPort(portName, baudRate, parity, dataBits, stopBits)
        {
            // RS485半双工：发送后立即清空输入缓冲，避免回显干扰
            ReadTimeout = 500,
            WriteTimeout = 500,
            // 禁用流控，纯RS485不使用RTS/CTS
            Handshake = Handshake.None,
            // 帧间最小静默时间（3.5个字符时间），此处由调用方控制
            ReceivedBytesThreshold = 1,
        };
    }

    /// <inheritdoc/>
    public void Open()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_serialPort.IsOpen)
        {
            return;
        }

        _serialPort.Open();
        _serialPort.DiscardInBuffer();
        _serialPort.DiscardOutBuffer();
        _logger.LogInformation(
            "{Tag} RS485 port {Port} opened at baud rate {Baud}",
            LogTag,
            _serialPort.PortName,
            _serialPort.BaudRate
        );
    }

    /// <inheritdoc/>
    public void Close()
    {
        if (!_serialPort.IsOpen)
        {
            return;
        }

        _serialPort.Close();
        _logger.LogInformation("{Tag} RS485 port {Port} closed", LogTag, _serialPort.PortName);
    }

    /// <inheritdoc/>
    public async Task<byte[]> SendAndReceiveAsync(
        byte[] request,
        int expectedResponseLength,
        int timeoutMs = 500,
        CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_serialPort.IsOpen)
        {
            throw new InvalidOperationException(
                $"{LogTag} RS485 port {PortName} is not open. Call Open() first."
            );
        }

        // 获取总线互斥锁，确保半双工时序正确
        await _busSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 清空残留数据，避免上次通信的残留影响本次响应
            _serialPort.DiscardInBuffer();
            _serialPort.DiscardOutBuffer();

            // 发送请求帧
            await _serialPort
                .BaseStream.WriteAsync(request, cancellationToken)
                .ConfigureAwait(false);
            await _serialPort.BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogDebug(
                "{Tag} RS485 TX [{Port}]: {Hex}",
                LogTag,
                PortName,
                Convert.ToHexString(request, 0, request.Length)
            );

            if (expectedResponseLength == 0)
            {
                return [];
            }

            if (expectedResponseLength < 0)
            {
                byte[] idleResponse = await ReceiveUntilIdleAsync(timeoutMs, cancellationToken)
                    .ConfigureAwait(false);

                _logger.LogDebug(
                    "{Tag} RS485 RX [{Port}]: {Hex}",
                    LogTag,
                    PortName,
                    Convert.ToHexString(idleResponse)
                );
                return idleResponse;
            }

            // 等待并接收响应帧
            byte[] exactResponse = await ReceiveExactAsync(
                    expectedResponseLength,
                    timeoutMs,
                    cancellationToken
                )
                .ConfigureAwait(false);

            _logger.LogDebug(
                "{Tag} RS485 RX [{Port}]: {Hex}",
                LogTag,
                PortName,
                Convert.ToHexString(exactResponse)
            );
            return exactResponse;
        }
        finally
        {
            _busSemaphore.Release();
        }
    }

    /// <summary>
    /// 精确读取指定字节数的响应数据，带超时控制
    /// </summary>
    private async Task<byte[]> ReceiveExactAsync(
        int length,
        int timeoutMs,
        CancellationToken cancellationToken
    )
    {
        byte[] buffer = new byte[length];
        int received = 0;
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        cts.CancelAfter(timeoutMs);

        try
        {
            while (received < length)
            {
                int read = await _serialPort
                    .BaseStream.ReadAsync(buffer.AsMemory(received, length - received), cts.Token)
                    .ConfigureAwait(false);

                if (read == 0)
                {
                    throw new TimeoutException(
                        $"{LogTag} Read timeout on port {PortName}, received {received}/{length} bytes."
                    );
                }

                received += read;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"{LogTag} Response timeout on port {PortName} ({timeoutMs} ms), received {received}/{length} bytes."
            );
        }

        return buffer;
    }

    /// <summary>
    /// 读取串口响应直到短暂空闲或整体超时，适用于串口调试助手这类未知长度响应。
    /// </summary>
    private async Task<byte[]> ReceiveUntilIdleAsync(
        int timeoutMs,
        CancellationToken cancellationToken
    )
    {
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        timeoutCts.CancelAfter(timeoutMs);

        using MemoryStream stream = new();
        byte[] buffer = new byte[256];

        while (!timeoutCts.IsCancellationRequested)
        {
            try
            {
                using CancellationTokenSource idleCts = CancellationTokenSource.CreateLinkedTokenSource(
                    timeoutCts.Token
                );
                idleCts.CancelAfter(stream.Length == 0 ? timeoutMs : 30);
                int read = await _serialPort
                    .BaseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), idleCts.Token)
                    .ConfigureAwait(false);
                if (read > 0)
                {
                    stream.Write(buffer, 0, read);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        return stream.ToArray();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Close();
        _serialPort.Dispose();
        _busSemaphore.Dispose();
        _logger.LogDebug("{Tag} RS485Port ({Port}) disposed", LogTag, PortName);
    }
}
