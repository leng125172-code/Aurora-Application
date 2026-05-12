namespace AuroraStruct3D.RS485.Modbus;

/// <summary>
/// Modbus CRC16 校验算法工具类（CRC-16/IBM，多项式 0x8005 反转形式 0xA001）
/// </summary>
public static class ModbusCrc16
{
    /// <summary>CRC16 查表，提升计算性能</summary>
    private static readonly ushort[] CrcTable = BuildCrcTable();

    /// <summary>
    /// 计算数据的 Modbus CRC16 校验值
    /// </summary>
    /// <param name="data">待校验的字节序列</param>
    /// <returns>CRC16 校验值（低字节在前，高字节在后，符合 Modbus RTU 规范）</returns>
    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            crc = (ushort)((crc >> 8) ^ CrcTable[(crc ^ b) & 0xFF]);
        }

        return crc;
    }

    /// <summary>
    /// 在字节数组末尾追加两字节 CRC（低字节在前）
    /// </summary>
    /// <param name="data">不含 CRC 的数据帧</param>
    /// <returns>追加 CRC 后的完整帧</returns>
    public static byte[] AppendCrc(byte[] data)
    {
        ushort crc = Compute(data);
        byte[] result = new byte[data.Length + 2];
        data.CopyTo(result, 0);
        result[^2] = (byte)(crc & 0xFF); // CRC 低字节
        result[^1] = (byte)((crc >> 8) & 0xFF); // CRC 高字节
        return result;
    }

    /// <summary>
    /// 校验帧尾的 CRC 是否正确
    /// </summary>
    /// <param name="frame">含尾部 CRC 的完整帧</param>
    /// <returns>true 表示 CRC 正确</returns>
    public static bool Validate(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 4)
        {
            return false;
        }

        ushort computed = Compute(frame[..^2]);
        ushort received = (ushort)(frame[^2] | (frame[^1] << 8));
        return computed == received;
    }

    /// <summary>
    /// 构建 CRC16 查找表
    /// </summary>
    private static ushort[] BuildCrcTable()
    {
        ushort[] table = new ushort[256];
        for (int i = 0; i < 256; i++)
        {
            ushort crc = (ushort)i;
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 0x0001) != 0)
                {
                    crc = (ushort)((crc >> 1) ^ 0xA001);
                }
                else
                {
                    crc >>= 1;
                }
            }

            table[i] = crc;
        }

        return table;
    }
}
