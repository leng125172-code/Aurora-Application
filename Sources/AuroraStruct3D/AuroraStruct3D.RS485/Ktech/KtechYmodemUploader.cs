using AuroraStruct3D.RS485.Protocol;
using Microsoft.Extensions.Logging;

namespace AuroraStruct3D.RS485.Ktech;

/// <summary>
/// Ymodem 固件升级进度事件参数
/// </summary>
public class KtechUpgradeProgress
{
    /// <summary>百分比 0-100</summary>
    public int Percent { get; init; }

    /// <summary>已发送字节</summary>
    public long BytesSent { get; init; }

    /// <summary>文件总长度</summary>
    public long TotalBytes { get; init; }

    /// <summary>当前阶段（如 "WaitC" / "SendingData" / "Finishing"）</summary>
    public string Stage { get; init; } = string.Empty;
}

/// <summary>
/// Ymodem 升级异常
/// </summary>
public class KtechYmodemException(string message, int errorStep) : Exception(message)
{
    /// <summary>错误步骤编号（对应 Demo 中的 errorType 1-10）</summary>
    public int ErrorStep { get; } = errorStep;
}

/// <summary>
/// 瓴控 KTECH 固件 Ymodem 上传器
/// </summary>
/// <remarks>
/// 与 <c>Documents/瓴控Demo/serovMotor/Ymodem.cs</c> 流程 1:1 对齐：
/// 1. 发 CMD_BeginIap(0xBB) 启动 IAP 后调用本类
/// 2. 等待设备发 'C' (0x43)
/// 3. SOH 首包 (128B 文件名+大小) + 等 ACK + 等 'C'
/// 4. 多个 STX 包 (1024B 数据 + CRC16) + 等 ACK，最后不足 1024 用 0xFF 填充
/// 5. EOT → 等 NAK → 再 EOT → 等 ACK → 等 'C' → 结束包（全 0 的 SOH）→ 等 ACK
/// </remarks>
public class KtechYmodemUploader
{
    private const byte Soh = 0x01;
    private const byte Stx = 0x02;
    private const byte Eot = 0x04;
    private const byte Ack = 0x06;
    private const byte Nak = 0x15;
    private const byte CChar = 0x43;

    private readonly IRS485Port _port;
    private readonly ILogger _logger;

    /// <summary>
    /// 初始化 Ymodem 上传器
    /// </summary>
    public KtechYmodemUploader(IRS485Port port, ILogger logger)
    {
        _port = port;
        _logger = logger;
    }

    /// <summary>
    /// 执行 Ymodem 升级流程
    /// </summary>
    /// <param name="fileName">固件文件名（不含路径，用于 SOH 首包）</param>
    /// <param name="content">固件二进制内容</param>
    /// <param name="progress">进度回调</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task UploadAsync(
        string fileName,
        ReadOnlyMemory<byte> content,
        IProgress<KtechUpgradeProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new KtechYmodemException("Ymodem 升级失败：文件名为空", 1);
        }
        if (content.Length == 0)
        {
            throw new KtechYmodemException("Ymodem 升级失败：固件内容为空", 1);
        }

        using IDisposable busLock = await _port
            .AcquireBusLockAsync(cancellationToken)
            .ConfigureAwait(false);
        _port.DiscardInputBuffer();

        progress?.Report(
            new KtechUpgradeProgress
            {
                Stage = "WaitInitialC",
                Percent = 0,
                TotalBytes = content.Length,
            }
        );
        if (!await WaitByteAsync(CChar, 2000, cancellationToken).ConfigureAwait(false))
        {
            throw new KtechYmodemException("等待设备发起 'C' 字符超时", 3);
        }

        // 1. SOH 首包：文件名 + 长度
        byte[] header = BuildHeaderPayload(fileName, content.Length);
        await SendPacketAsync(Soh, 0, header, cancellationToken).ConfigureAwait(false);

        if (!await WaitByteAsync(Ack, 2000, cancellationToken).ConfigureAwait(false))
        {
            throw new KtechYmodemException("SOH 首包未收到 ACK", 4);
        }
        if (!await WaitByteAsync(CChar, 200, cancellationToken).ConfigureAwait(false))
        {
            throw new KtechYmodemException("SOH 首包后等待 'C' 超时", 5);
        }

        // 2. STX 数据包循环
        byte packetNumber = 0;
        long offset = 0;
        byte[] block = new byte[1024];
        int totalLen = content.Length;

        while (offset < totalLen)
        {
            int chunk = (int)Math.Min(1024L, totalLen - offset);
            content.Span.Slice((int)offset, chunk).CopyTo(block);
            if (chunk < 1024)
            {
                for (int i = chunk; i < 1024; i++)
                {
                    block[i] = 0xFF;
                }
            }
            packetNumber++;
            await SendPacketAsync(Stx, packetNumber, block, cancellationToken)
                .ConfigureAwait(false);

            offset += chunk;
            int percent = (int)Math.Min(100, offset * 100 / totalLen);
            progress?.Report(
                new KtechUpgradeProgress
                {
                    Stage = "SendingData",
                    Percent = percent,
                    BytesSent = offset,
                    TotalBytes = totalLen,
                }
            );

            if (!await WaitByteAsync(Ack, 1000, cancellationToken).ConfigureAwait(false))
            {
                throw new KtechYmodemException(
                    $"数据包 #{packetNumber} 未收到 ACK（offset={offset}）",
                    6
                );
            }
        }

        // 3. EOT → NAK → EOT → ACK → C → 结束包 → ACK
        progress?.Report(
            new KtechUpgradeProgress
            {
                Stage = "Finishing",
                Percent = 100,
                BytesSent = offset,
                TotalBytes = totalLen,
            }
        );

        await _port
            .WriteRawAsync(new byte[] { Eot }, 1000, cancellationToken)
            .ConfigureAwait(false);
        if (!await WaitByteAsync(Nak, 1000, cancellationToken).ConfigureAwait(false))
        {
            throw new KtechYmodemException("首次 EOT 未收到 NAK", 7);
        }
        await _port
            .WriteRawAsync(new byte[] { Eot }, 1000, cancellationToken)
            .ConfigureAwait(false);
        if (!await WaitByteAsync(Ack, 1000, cancellationToken).ConfigureAwait(false))
        {
            throw new KtechYmodemException("二次 EOT 未收到 ACK", 8);
        }
        if (!await WaitByteAsync(CChar, 1000, cancellationToken).ConfigureAwait(false))
        {
            throw new KtechYmodemException("EOT 后等待 'C' 超时", 9);
        }

        // 结束包：128 字节全 0 的 SOH 包，packetNumber=0
        byte[] endBlock = new byte[128];
        await SendPacketAsync(Soh, 0, endBlock, cancellationToken).ConfigureAwait(false);

        if (!await WaitByteAsync(Ack, 2000, cancellationToken).ConfigureAwait(false))
        {
            throw new KtechYmodemException("结束包未收到 ACK", 10);
        }

        progress?.Report(
            new KtechUpgradeProgress
            {
                Stage = "Done",
                Percent = 100,
                BytesSent = totalLen,
                TotalBytes = totalLen,
            }
        );
        _logger.LogInformation(
            "[KTECH-Ymodem] Firmware upload completed: {FileName} ({Bytes} bytes)",
            fileName,
            totalLen
        );
    }

    /// <summary>
    /// 构造 SOH 首包 payload：文件名 + '\0' + 长度字符串 + '\0' + 0 填充至 128 字节
    /// </summary>
    private static byte[] BuildHeaderPayload(string fileName, long fileLength)
    {
        byte[] block = new byte[128];
        int i = 0;
        foreach (char c in fileName)
        {
            if (c == 0 || i >= 127)
            {
                break;
            }
            block[i++] = (byte)c;
        }
        block[i++] = 0;
        string lenStr = fileLength.ToString();
        foreach (char c in lenStr)
        {
            if (c == 0 || i >= 127)
            {
                break;
            }
            block[i++] = (byte)c;
        }
        if (i < 128)
        {
            block[i] = 0;
        }
        return block;
    }

    /// <summary>
    /// 构造并发送一个 Ymodem 数据包：[Header][PktNum][~PktNum][Data 128/1024][CRC16 大端]
    /// </summary>
    private async Task SendPacketAsync(
        byte header,
        byte packetNumber,
        byte[] data,
        CancellationToken cancellationToken
    )
    {
        int dataSize = header == Soh ? 128 : 1024;
        byte[] packet = new byte[3 + dataSize + 2];
        packet[0] = header;
        packet[1] = packetNumber;
        packet[2] = (byte)(255 - packetNumber);
        Array.Copy(data, 0, packet, 3, dataSize);
        ushort crc = KtechCrc.ComputeCrc16(data.AsSpan(0, dataSize));
        packet[^2] = (byte)((crc >> 8) & 0xFF);
        packet[^1] = (byte)(crc & 0xFF);

        // 大块写入超时按数据大小给宽裕值：1024B @ 9600bps 也只需约 1s
        int writeTimeoutMs = dataSize == 1024 ? 5000 : 2000;
        await _port.WriteRawAsync(packet, writeTimeoutMs, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 在指定时间窗口内等待收到目标字节
    /// </summary>
    private async Task<bool> WaitByteAsync(
        byte target,
        int timeoutMs,
        CancellationToken cancellationToken
    )
    {
        long start = Environment.TickCount64;
        while (Environment.TickCount64 - start < timeoutMs)
        {
            int b = await _port.ReadRawByteAsync(10, cancellationToken).ConfigureAwait(false);
            if (b == target)
            {
                return true;
            }
        }
        return false;
    }
}
