using System.Diagnostics;
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

            // 发送请求帧。说明：Linux 下 SerialPort.WriteTimeout 对 BaseStream.WriteAsync
            // 并不总是生效（无应答/未接终端时可能永久阻塞），这里额外用 linked CTS 强制超时，
            // 避免单个串口卡死整个扫描流程
            using (
                CancellationTokenSource writeCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken
                )
            )
            {
                writeCts.CancelAfter(timeoutMs);
                await _serialPort
                    .BaseStream.WriteAsync(request, writeCts.Token)
                    .ConfigureAwait(false);
                await _serialPort.BaseStream.FlushAsync(writeCts.Token).ConfigureAwait(false);
            }

            _logger.LogDebug(
                "{Tag} RS485 TX [{Port}]: {Hex}",
                LogTag,
                PortName,
                FormatHexForLog(request)
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
                    FormatHexForLog(idleResponse)
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
                FormatHexForLog(exactResponse)
            );
            return exactResponse;
        }
        finally
        {
            _busSemaphore.Release();
        }
    }

    private static string FormatHexForLog(ReadOnlySpan<byte> data)
    {
        return data.Length == 0
            ? string.Empty
            : BitConverter.ToString(data.ToArray()).Replace('-', ' ');
    }

    /// <summary>
    /// 精确读取指定字节数的响应数据，带超时控制。
    /// 实现说明：采用 <see cref="SerialPort.BytesToRead"/> 轮询而非向 BaseStream.ReadAsync
    /// 传入 CancellationToken，规避 .NET SerialStream 在 Token 取消时抛 IOException 并
    /// 将底层流拖入异常态的问题（与 <see cref="ReceiveUntilIdleAsync"/> 同理）。
    /// </summary>
    private async Task<byte[]> ReceiveExactAsync(
        int length,
        int timeoutMs,
        CancellationToken cancellationToken
    )
    {
        byte[] buffer = new byte[length];
        int received = 0;
        Stopwatch sw = Stopwatch.StartNew();

        while (received < length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (sw.ElapsedMilliseconds >= timeoutMs)
            {
                throw new TimeoutException(
                    $"{LogTag} Response timeout on port {PortName} ({timeoutMs} ms), received {received}/{length} bytes."
                );
            }

            int available = _serialPort.BytesToRead;
            if (available > 0)
            {
                int toRead = Math.Min(available, length - received);
                int read = _serialPort.Read(buffer, received, toRead);
                received += read;
            }
            else
            {
                await Task.Delay(2, cancellationToken).ConfigureAwait(false);
            }
        }

        return buffer;
    }

    /// <summary>
    /// 读取串口响应直到短暂空闲或整体超时，适用于串口调试助手这类未知长度响应。
    /// 实现说明：采用 <see cref="SerialPort.BytesToRead"/> 轮询而非 cancel pending ReadAsync。
    /// 原因是 .NET 的 <c>SerialStream.EndRead</c> 在 CancellationToken 取消时会抛
    /// <see cref="IOException"/>("The I/O operation has been aborted")，并把底层流拖入异常态，
    /// 影响后续读写。轮询方案规避此坑。
    /// </summary>
    private async Task<byte[]> ReceiveUntilIdleAsync(
        int timeoutMs,
        CancellationToken cancellationToken
    )
    {
        // 首字节最长等待 timeoutMs；首字节到达后，30ms 内未有新字节则视为空闲完成
        const int IdleQuietMs = 30;
        const int PollIntervalMs = 5;

        Stopwatch overall = Stopwatch.StartNew();
        Stopwatch sinceLast = new();
        using MemoryStream stream = new();
        byte[] tmp = new byte[256];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int available = _serialPort.BytesToRead;
            if (available > 0)
            {
                int toRead = Math.Min(available, tmp.Length);
                int read = _serialPort.Read(tmp, 0, toRead);
                if (read > 0)
                {
                    stream.Write(tmp, 0, read);
                    sinceLast.Restart();
                }
            }
            else
            {
                // 无数据可读：根据是否已有累计数据，判断是用整体超时还是空闲超时
                if (stream.Length == 0)
                {
                    if (overall.ElapsedMilliseconds >= timeoutMs)
                    {
                        break;
                    }
                }
                else if (sinceLast.ElapsedMilliseconds >= IdleQuietMs)
                {
                    break;
                }

                await Task.Delay(PollIntervalMs, cancellationToken).ConfigureAwait(false);
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

    /// <inheritdoc/>
    public async Task<IDisposable> AcquireBusLockAsync(
        CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _busSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new BusLockReleaser(_busSemaphore);
    }

    /// <inheritdoc/>
    public async Task WriteRawAsync(
        ReadOnlyMemory<byte> data,
        int timeoutMs,
        CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_serialPort.IsOpen)
        {
            throw new InvalidOperationException($"{LogTag} RS485 port {PortName} is not open.");
        }
        using CancellationTokenSource writeCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        writeCts.CancelAfter(timeoutMs);
        await _serialPort.BaseStream.WriteAsync(data, writeCts.Token).ConfigureAwait(false);
        await _serialPort.BaseStream.FlushAsync(writeCts.Token).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<int> ReadRawByteAsync(
        int timeoutMs,
        CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_serialPort.IsOpen)
        {
            throw new InvalidOperationException($"{LogTag} RS485 port {PortName} is not open.");
        }
        byte[] one = new byte[1];
        using CancellationTokenSource readCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        readCts.CancelAfter(timeoutMs);
        try
        {
            int read = await _serialPort
                .BaseStream.ReadAsync(one.AsMemory(0, 1), readCts.Token)
                .ConfigureAwait(false);
            return read > 0 ? one[0] : -1;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return -1;
        }
    }

    /// <inheritdoc/>
    public void DiscardInputBuffer()
    {
        if (_serialPort.IsOpen)
        {
            _serialPort.DiscardInBuffer();
        }
    }

    /// <summary>总线锁释放器</summary>
    private sealed class BusLockReleaser : IDisposable
    {
        private SemaphoreSlim? _semaphore;

        public BusLockReleaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            SemaphoreSlim? sem = Interlocked.Exchange(ref _semaphore, null);
            sem?.Release();
        }
    }
}
