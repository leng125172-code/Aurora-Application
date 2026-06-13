using System.Diagnostics;
using System.IO.Ports;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace AuroraStruct3D.RS485.Protocol;

/// <summary>
/// 基于优先级命令队列的 RS485 串口通信实现。
/// <para>
/// 架构特性：
/// <list type="bullet">
///   <item>独立发送工作线程，上层调用全程非阻塞</item>
///   <item>双队列优先级调度：紧急命令（<see cref="SendAndReceiveUrgentAsync"/>）跳过普通命令队列优先执行</item>
///   <item>普通命令支持配置最大重发次数，超时自动重发，重发次数耗尽后上报失败</item>
///   <item>串口硬件异常时自动关闭并按配置间隔持续重连，重连期间队列命令正常入队等待</item>
///   <item>Ymodem 等直接访问场景通过 <see cref="AcquireBusLockAsync"/> 暂停工作线程独占总线</item>
///   <item>全链路监控指标通过 <see cref="GetMetrics"/> 实时读取</item>
/// </list>
/// </para>
/// </summary>
public sealed class RS485Port : IRS485Port
{
    private const string LogTag = "[Serial port]";

    // ─── 串口硬件 ─────────────────────────────────────────────────────────────

    private readonly SerialPort _serialPort;
    private readonly ILogger<RS485Port> _logger;
    private readonly int _maxRetries;
    private readonly TimeSpan _reconnectInterval;

    // ─── 命令优先级队列 ───────────────────────────────────────────────────────

    /// <summary>紧急命令队列（无界，急停等实时指令插队此处）</summary>
    private readonly Channel<PendingRS485Command> _urgentChannel =
        Channel.CreateUnbounded<PendingRS485Command>(
            new UnboundedChannelOptions { SingleWriter = false, SingleReader = true }
        );

    /// <summary>普通命令队列（有界 256，防止调用方无限积压）</summary>
    private readonly Channel<PendingRS485Command> _normalChannel =
        Channel.CreateBounded<PendingRS485Command>(
            new BoundedChannelOptions(256)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = false,
                SingleReader = true,
            }
        );

    // ─── 总线独占锁（工作线程 + Ymodem 互斥）────────────────────────────────

    /// <summary>
    /// 执行独占锁：工作线程每条命令执行前获取，执行后释放；
    /// Ymodem 通过 AcquireBusLockAsync 长期持有，期间工作线程命令阻塞在此锁上，
    /// 确保 Ymodem 对总线字节流的完全独占控制。
    /// </summary>
    private readonly SemaphoreSlim _exclusiveLock = new(1, 1);

    // ─── 工作线程 ─────────────────────────────────────────────────────────────

    private readonly CancellationTokenSource _workerCts = new();
    private readonly Task _workerTask;

    // ─── 监控指标（Interlocked 原子操作）────────────────────────────────────

    private long _totalSent;
    private long _totalTimeouts;
    private long _totalRetries;
    private long _totalReconnects;

    // ─── 是否已释放 ───────────────────────────────────────────────────────────

    private bool _disposed;

    // ─── 属性 ─────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public string PortName => _serialPort.PortName;

    /// <inheritdoc/>
    public bool IsOpen => _serialPort.IsOpen;

    /// <summary>
    /// 初始化RS485串口实例并启动工作线程
    /// </summary>
    /// <param name="portName">串口设备路径（如 /dev/ttyS6 或 COM1）</param>
    /// <param name="baudRate">波特率</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="parity">校验位，默认无校验</param>
    /// <param name="dataBits">数据位，默认8位</param>
    /// <param name="stopBits">停止位，默认1位</param>
    /// <param name="maxRetries">普通命令超时后最大自动重发次数，默认2次</param>
    /// <param name="reconnectIntervalSeconds">串口异常后重连间隔（秒），默认1秒</param>
    public RS485Port(
        string portName,
        int baudRate,
        ILogger<RS485Port> logger,
        Parity parity = Parity.None,
        int dataBits = 8,
        StopBits stopBits = StopBits.One,
        int maxRetries = 2,
        int reconnectIntervalSeconds = 1
    )
    {
        _logger = logger;
        _maxRetries = maxRetries;
        _reconnectInterval = TimeSpan.FromSeconds(reconnectIntervalSeconds);

        _serialPort = new SerialPort(portName, baudRate, parity, dataBits, stopBits)
        {
            ReadTimeout = 500,
            WriteTimeout = 500,
            Handshake = Handshake.None,
            ReceivedBytesThreshold = 1,
        };

        // 启动独立工作线程（LongRunning 避免占用 ThreadPool 线程）
        _workerTask = Task.Factory.StartNew(
            () => WorkerLoopAsync(_workerCts.Token).GetAwaiter().GetResult(),
            _workerCts.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default
        );
    }

    // ─── IRS485Port 公共接口 ──────────────────────────────────────────────────

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
            PortName,
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
        _logger.LogInformation("{Tag} RS485 port {Port} closed", LogTag, PortName);
    }

    /// <inheritdoc/>
    public Task<byte[]> SendAndReceiveAsync(
        byte[] request,
        int expectedResponseLength,
        int timeoutMs = 500,
        CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return EnqueueAsync(
            request,
            expectedResponseLength,
            timeoutMs,
            urgent: false,
            _maxRetries,
            cancellationToken
        );
    }

    /// <inheritdoc/>
    public Task<byte[]> SendAndReceiveUrgentAsync(
        byte[] request,
        int expectedResponseLength,
        int timeoutMs = 200,
        CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // 紧急命令不重发（急停场景一次超时即上报）
        return EnqueueAsync(
            request,
            expectedResponseLength,
            timeoutMs,
            urgent: true,
            maxRetries: 0,
            cancellationToken
        );
    }

    /// <inheritdoc/>
    public RS485PortMetrics GetMetrics()
    {
        return new RS485PortMetrics
        {
            IsConnected = _serialPort.IsOpen,
            PendingUrgentCount = _urgentChannel.Reader.Count,
            PendingNormalCount = _normalChannel.Reader.Count,
            TotalSent = Interlocked.Read(ref _totalSent),
            TotalTimeouts = Interlocked.Read(ref _totalTimeouts),
            TotalRetries = Interlocked.Read(ref _totalRetries),
            TotalReconnects = Interlocked.Read(ref _totalReconnects),
        };
    }

    /// <inheritdoc/>
    public async Task<IDisposable> AcquireBusLockAsync(
        CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // 等待工作线程当前命令完成后独占总线
        await _exclusiveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new BusLockReleaser(_exclusiveLock);
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

    // ─── 命令入队 ─────────────────────────────────────────────────────────────

    private async Task<byte[]> EnqueueAsync(
        byte[] request,
        int expectedResponseLength,
        int timeoutMs,
        bool urgent,
        int maxRetries,
        CancellationToken cancellationToken
    )
    {
        PendingRS485Command cmd = new()
        {
            RequestBytes = request,
            ExpectedResponseLength = expectedResponseLength,
            TimeoutMs = timeoutMs,
            MaxRetries = maxRetries,
            IsUrgent = urgent,
            CallerToken = cancellationToken,
        };

        ChannelWriter<PendingRS485Command> writer = urgent
            ? _urgentChannel.Writer
            : _normalChannel.Writer;

        await writer.WriteAsync(cmd, cancellationToken).ConfigureAwait(false);

        // 等待工作线程处理完成并返回结果（或异常）
        return await cmd.Tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    // ─── 工作线程主循环 ───────────────────────────────────────────────────────

    private async Task WorkerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 串口未打开时进入重连循环
                if (!_serialPort.IsOpen)
                {
                    await TryReconnectAsync(ct).ConfigureAwait(false);
                    if (!_serialPort.IsOpen)
                    {
                        continue;
                    }
                }

                // ① 优先消费紧急队列
                if (_urgentChannel.Reader.TryRead(out PendingRS485Command? urgent))
                {
                    await ExecuteCommandAsync(urgent, ct).ConfigureAwait(false);
                    continue;
                }

                // ② 消费普通队列，但执行前先检查紧急队列（避免紧急命令在普通命令队列后等待）
                if (_normalChannel.Reader.TryRead(out PendingRS485Command? normal))
                {
                    while (_urgentChannel.Reader.TryRead(out urgent))
                    {
                        await ExecuteCommandAsync(urgent, ct).ConfigureAwait(false);
                    }
                    await ExecuteCommandAsync(normal, ct).ConfigureAwait(false);
                    continue;
                }

                // ③ 两个队列均空，等待（最多 200ms，防止错过重连窗口）
                try
                {
                    using CancellationTokenSource waitCts =
                        CancellationTokenSource.CreateLinkedTokenSource(ct);
                    waitCts.CancelAfter(200);
                    await Task.WhenAny(
                            _urgentChannel.Reader.WaitToReadAsync(waitCts.Token).AsTask(),
                            _normalChannel.Reader.WaitToReadAsync(waitCts.Token).AsTask()
                        )
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // 200ms 超时正常，继续循环
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Tag} [{Port}] 工作线程意外异常", LogTag, PortName);
                await Task.Delay(100, ct).ConfigureAwait(false);
            }
        }

        DrainAllQueues();
    }

    // ─── 命令执行（含重发逻辑）───────────────────────────────────────────────

    private async Task ExecuteCommandAsync(PendingRS485Command cmd, CancellationToken workerCt)
    {
        // 获取总线独占锁，与 Ymodem 等直接访问场景互斥
        await _exclusiveLock.WaitAsync(workerCt).ConfigureAwait(false);
        try
        {
            while (true) // 重发循环
            {
                if (cmd.CallerToken.IsCancellationRequested)
                {
                    cmd.Tcs.TrySetCanceled(cmd.CallerToken);
                    return;
                }

                try
                {
                    _serialPort.DiscardInBuffer();
                    _serialPort.DiscardOutBuffer();

                    using CancellationTokenSource writeCts =
                        CancellationTokenSource.CreateLinkedTokenSource(cmd.CallerToken, workerCt);
                    writeCts.CancelAfter(cmd.TimeoutMs);

                    await _serialPort
                        .BaseStream.WriteAsync(cmd.RequestBytes, writeCts.Token)
                        .ConfigureAwait(false);
                    await _serialPort.BaseStream.FlushAsync(writeCts.Token).ConfigureAwait(false);

                    _logger.LogDebug(
                        "{Tag} TX [{Port}] Cmd={Id} Urgent={Urgent}: {Hex}",
                        LogTag,
                        PortName,
                        cmd.CommandId,
                        cmd.IsUrgent,
                        FormatHexForLog(cmd.RequestBytes)
                    );

                    byte[] response;
                    if (cmd.ExpectedResponseLength == 0)
                    {
                        response = [];
                    }
                    else if (cmd.ExpectedResponseLength < 0)
                    {
                        response = await ReceiveUntilIdleAsync(cmd.TimeoutMs, cmd.CallerToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        response = await ReceiveExactAsync(
                                cmd.ExpectedResponseLength,
                                cmd.TimeoutMs,
                                cmd.CallerToken
                            )
                            .ConfigureAwait(false);
                    }

                    _logger.LogDebug(
                        "{Tag} RX [{Port}] Cmd={Id}: {Hex}",
                        LogTag,
                        PortName,
                        cmd.CommandId,
                        FormatHexForLog(response)
                    );

                    Interlocked.Increment(ref _totalSent);
                    cmd.Tcs.TrySetResult(response);
                    return;
                }
                catch (TimeoutException)
                {
                    Interlocked.Increment(ref _totalTimeouts);

                    if (cmd.RetryCount < cmd.MaxRetries)
                    {
                        cmd.RetryCount++;
                        Interlocked.Increment(ref _totalRetries);
                        _logger.LogWarning(
                            "{Tag} [{Port}] Cmd={Id} 超时，第 {Retry}/{Max} 次重发",
                            LogTag,
                            PortName,
                            cmd.CommandId,
                            cmd.RetryCount,
                            cmd.MaxRetries
                        );
                        continue; // 重试
                    }

                    cmd.Tcs.TrySetException(
                        new TimeoutException(
                            $"{LogTag} [{PortName}] 命令 {cmd.CommandId} 超时（{cmd.TimeoutMs}ms），"
                                + $"已重发 {cmd.RetryCount} 次，请确认设备连接和地址配置正确"
                        )
                    );
                    return;
                }
                catch (OperationCanceledException) when (cmd.CallerToken.IsCancellationRequested)
                {
                    cmd.Tcs.TrySetCanceled(cmd.CallerToken);
                    return;
                }
                catch (Exception ex)
                {
                    // 串口硬件异常：标记命令失败并触发重连
                    _logger.LogWarning(
                        ex,
                        "{Tag} [{Port}] Cmd={Id} 串口硬件异常，触发自动重连",
                        LogTag,
                        PortName,
                        cmd.CommandId
                    );
                    cmd.Tcs.TrySetException(ex);
                    SafeClose();
                    return;
                }
            }
        }
        finally
        {
            _exclusiveLock.Release();
        }
    }

    // ─── 自动重连 ─────────────────────────────────────────────────────────────

    private async Task TryReconnectAsync(CancellationToken ct)
    {
        _logger.LogInformation(
            "{Tag} [{Port}] 串口已断开，每 {Interval} 尝试一次重连...",
            LogTag,
            PortName,
            _reconnectInterval
        );

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 若串口已被其他路径打开（如并发重连），直接视为重连成功
                if (_serialPort.IsOpen)
                {
                    _logger.LogInformation(
                        "{Tag} [{Port}] 串口已处于打开状态，跳过重连",
                        LogTag,
                        PortName
                    );
                    return;
                }

                _serialPort.Open();
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();
                long count = Interlocked.Increment(ref _totalReconnects);
                _logger.LogInformation(
                    "{Tag} [{Port}] 串口重连成功（第 {Count} 次）",
                    LogTag,
                    PortName,
                    count
                );
                return;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "{Tag} [{Port}] 重连失败，{Interval} 后重试",
                    LogTag,
                    PortName,
                    _reconnectInterval
                );
                try
                {
                    await Task.Delay(_reconnectInterval, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private void SafeClose()
    {
        try
        {
            if (_serialPort.IsOpen)
            {
                _serialPort.Close();
            }
        }
        catch
        {
            // 关闭过程中的异常静默忽略
        }
    }

    private void DrainAllQueues()
    {
        var cancelError = new OperationCanceledException("RS485 串口已关闭，命令队列已清空");
        while (_urgentChannel.Reader.TryRead(out PendingRS485Command? cmd))
        {
            cmd.Tcs.TrySetException(cancelError);
        }
        while (_normalChannel.Reader.TryRead(out PendingRS485Command? cmd))
        {
            cmd.Tcs.TrySetException(cancelError);
        }
    }

    // ─── 接收辅助方法 ─────────────────────────────────────────────────────────

    private static string FormatHexForLog(ReadOnlySpan<byte> data)
    {
        return data.Length == 0
            ? string.Empty
            : BitConverter.ToString(data.ToArray()).Replace('-', ' ');
    }

    /// <summary>
    /// 精确读取指定字节数的响应数据，带超时控制。
    /// 采用 BytesToRead 轮询而非 ReadAsync + CancellationToken，规避 .NET SerialStream
    /// 在 Token 取消时抛 IOException 并将底层流拖入异常态的问题。
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
    /// 读取串口响应直到短暂空闲或整体超时（适用于未知长度响应）。
    /// 采用 BytesToRead 轮询规避 .NET SerialStream 取消异常问题。
    /// </summary>
    private async Task<byte[]> ReceiveUntilIdleAsync(
        int timeoutMs,
        CancellationToken cancellationToken
    )
    {
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

    // ─── Dispose ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _workerCts.Cancel();
        try
        {
            _workerTask.Wait(TimeSpan.FromSeconds(3));
        }
        catch
        {
            // 忽略工作线程退出异常
        }

        _workerCts.Dispose();
        _urgentChannel.Writer.TryComplete();
        _normalChannel.Writer.TryComplete();
        DrainAllQueues();
        SafeClose();
        _serialPort.Dispose();
        _exclusiveLock.Dispose();

        _logger.LogDebug("{Tag} RS485Port ({Port}) disposed", LogTag, PortName);
    }

    // ─── 总线锁释放器 ─────────────────────────────────────────────────────────

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
