namespace AuroraStruct3D.RS485.Ktech;

/// <summary>
/// 瓴控 Ymodem 协议使用的 CRC-16/XMODEM（多项式 0x1021，初始值 0x0000）
/// </summary>
public static class KtechCrc
{
    /// <summary>
    /// 计算 CRC-16/XMODEM 校验值（位递推，与 Demo General.CRC16_Cal 等价）
    /// </summary>
    public static ushort ComputeCrc16(ReadOnlySpan<byte> data, ushort initial = 0)
    {
        ushort crc = initial;
        foreach (byte b in data)
        {
            crc ^= (ushort)(b << 8);
            for (int i = 0; i < 8; i++)
            {
                if ((crc & 0x8000) != 0)
                {
                    crc = (ushort)((crc << 1) ^ 0x1021);
                }
                else
                {
                    crc <<= 1;
                }
            }
        }
        return crc;
    }
}
