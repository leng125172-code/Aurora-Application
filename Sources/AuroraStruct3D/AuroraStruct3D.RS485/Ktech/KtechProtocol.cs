namespace AuroraStruct3D.RS485.Ktech;

/// <summary>
/// 瓴控 KTECH 协议通用帧编解码（基于 Demo 文档：dataStreamClass.cs / General.cs）
/// </summary>
/// <remarks>
/// <para>帧格式（完整）：</para>
/// <code>
/// [帧头 0x3E] [命令码] [地址] [数据长度 N] [Header校验] [Data 0..N-1] [Data校验]
/// </code>
/// <para>无数据时帧长 5 字节；有数据时帧长 5+N+1 字节。</para>
/// <para>校验算法（Demo 默认）：</para>
/// <list type="bullet">
/// <item>HeaderChecksum = Sum(帧头 + 命令码 + 地址 + 数据长度) &amp; 0xFF</item>
/// <item>DataChecksum   = Sum(所有数据字节) &amp; 0xFF</item>
/// </list>
/// <para>注意：旧 <see cref="KtechFrame"/> 实现使用 ~(cmd+id+len) 取反算法，可能为另一硬件型号实测。
/// 本通用协议层用于覆盖 Demo 全功能，与之并存。</para>
/// </remarks>
public static class KtechProtocol
{
    /// <summary>帧头字节 '>'</summary>
    public const byte FrameHeader = 0x3E;

    /// <summary>
    /// 计算字节序列的累加和低 8 位
    /// </summary>
    public static byte ComputeChecksum(ReadOnlySpan<byte> data)
    {
        int sum = 0;
        foreach (byte b in data)
        {
            sum += b;
        }
        return (byte)(sum & 0xFF);
    }

    /// <summary>
    /// 构造完整命令帧
    /// </summary>
    /// <param name="slaveId">从机地址</param>
    /// <param name="command">命令码</param>
    /// <param name="payload">数据 payload（可为空）</param>
    /// <returns>完整命令帧字节数组</returns>
    public static byte[] BuildFrame(byte slaveId, KtechCommands command, ReadOnlySpan<byte> payload)
    {
        byte cmd = (byte)command;
        byte dataLen = (byte)payload.Length;
        byte headerChecksum = ComputeChecksum(
            stackalloc byte[] { FrameHeader, cmd, slaveId, dataLen }
        );

        if (payload.IsEmpty)
        {
            return [FrameHeader, cmd, slaveId, dataLen, headerChecksum];
        }

        byte dataChecksum = ComputeChecksum(payload);
        byte[] frame = new byte[5 + payload.Length + 1];
        frame[0] = FrameHeader;
        frame[1] = cmd;
        frame[2] = slaveId;
        frame[3] = dataLen;
        frame[4] = headerChecksum;
        payload.CopyTo(frame.AsSpan(5));
        frame[^1] = dataChecksum;
        return frame;
    }

    /// <summary>
    /// 解析响应帧并提取数据 payload
    /// </summary>
    /// <param name="frame">完整响应帧字节</param>
    /// <param name="expectedCommand">期望的命令码（响应通常回显请求的命令码）</param>
    /// <param name="expectedSlaveId">期望的从机地址</param>
    /// <returns>提取的 payload 字节（不包含帧头/校验等）</returns>
    /// <exception cref="InvalidDataException">帧格式或校验错误时抛出</exception>
    public static byte[] ParseResponse(
        ReadOnlySpan<byte> frame,
        KtechCommands expectedCommand,
        byte expectedSlaveId
    )
    {
        if (frame.Length < 5)
        {
            throw new InvalidDataException(
                $"KTECH 响应帧长度不足，至少 5 字节，实际 {frame.Length} 字节"
            );
        }

        if (frame[0] != FrameHeader)
        {
            throw new InvalidDataException(
                $"KTECH 帧头错误，期望 0x{FrameHeader:X2}，实际 0x{frame[0]:X2}"
            );
        }

        if (frame[1] != (byte)expectedCommand)
        {
            throw new InvalidDataException(
                $"KTECH 命令码不匹配，期望 0x{(byte)expectedCommand:X2}，实际 0x{frame[1]:X2}"
            );
        }

        if (frame[2] != expectedSlaveId)
        {
            throw new InvalidDataException(
                $"KTECH 从机地址不匹配，期望 {expectedSlaveId}，实际 {frame[2]}"
            );
        }

        byte dataLen = frame[3];

        // 校验帧头部分
        byte headerChecksum = ComputeChecksum(frame[..4]);
        if (frame[4] != headerChecksum)
        {
            throw new InvalidDataException(
                $"KTECH 帧头校验和错误，期望 0x{headerChecksum:X2}，实际 0x{frame[4]:X2}，帧: {Convert.ToHexString(frame)}"
            );
        }

        if (dataLen == 0)
        {
            return [];
        }

        int expectedTotalLen = 5 + dataLen + 1;
        if (frame.Length < expectedTotalLen)
        {
            throw new InvalidDataException(
                $"KTECH 响应帧长度不足，期望 {expectedTotalLen} 字节，实际 {frame.Length} 字节"
            );
        }

        ReadOnlySpan<byte> payload = frame.Slice(5, dataLen);
        byte dataChecksum = ComputeChecksum(payload);
        if (frame[5 + dataLen] != dataChecksum)
        {
            throw new InvalidDataException(
                $"KTECH 数据校验和错误，期望 0x{dataChecksum:X2}，实际 0x{frame[5 + dataLen]:X2}，帧: {Convert.ToHexString(frame)}"
            );
        }

        return payload.ToArray();
    }

    /// <summary>
    /// 从响应字节流中尝试读取一帧完整数据。
    /// 用于处理收到的字节可能多于/少于单帧的场景。
    /// </summary>
    /// <param name="buffer">响应字节缓冲（连续接收）</param>
    /// <param name="consumed">本次成功解析后消费的字节数</param>
    /// <returns>是否解析到一个完整帧</returns>
    public static bool TryReadFrame(ReadOnlySpan<byte> buffer, out int consumed)
    {
        consumed = 0;
        if (buffer.Length < 5)
        {
            return false;
        }
        // 寻找帧头
        int headerIndex = -1;
        for (int i = 0; i <= buffer.Length - 5; i++)
        {
            if (buffer[i] == FrameHeader)
            {
                headerIndex = i;
                break;
            }
        }
        if (headerIndex < 0)
        {
            return false;
        }

        if (buffer.Length - headerIndex < 5)
        {
            return false;
        }

        byte dataLen = buffer[headerIndex + 3];
        int frameLen = dataLen == 0 ? 5 : 5 + dataLen + 1;
        if (buffer.Length - headerIndex < frameLen)
        {
            return false;
        }

        consumed = headerIndex + frameLen;
        return true;
    }

    /// <summary>
    /// 计算预期响应帧的总长度
    /// </summary>
    /// <param name="expectedPayloadLength">预期 payload 长度（不含校验）</param>
    /// <returns>预期帧总长度</returns>
    public static int ExpectedResponseLength(int expectedPayloadLength)
    {
        return expectedPayloadLength == 0 ? 5 : 5 + expectedPayloadLength + 1;
    }
}
