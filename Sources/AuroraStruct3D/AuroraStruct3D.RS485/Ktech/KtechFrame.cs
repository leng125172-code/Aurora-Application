namespace AuroraStruct3D.RS485.Ktech;

/// <summary>
/// 瓴控KTECH电机 CMD 0x9A 帧定义与校验工具
/// </summary>
/// <remarks>
/// KTECH私有协议帧结构（13字节响应示例）：
/// [3e] [9a] [SlaveId] [B3] [B4] [B5] [B6] [B7] [B8] [B9] [B10] [B11] [CHK]
///  帧头  CMD  从机地址  ← 速度(uint16) → ← 位置(uint16) →   状态字节组         校验和
///
/// 校验和算法（从CMD字节到最后数据字节的累加和 XOR 0x82）：
/// CHK = (Σ bytes[1..11]) &amp; 0xFF ^ 0x82
/// 注：此算法根据已知两帧数据推导得出，建议与瓴控官方文档核对。
/// </remarks>
public static class KtechFrame
{
    /// <summary>帧头字节 '>'</summary>
    public const byte FrameHeader = 0x3E;

    /// <summary>查询状态命令字</summary>
    public const byte CmdQueryStatus = 0x9A;

    /// <summary>电机运行命令（从关闭状态切换到运行状态，开启电机输出）</summary>
    public const byte CmdRun = 0x88;

    /// <summary>电机关闭命令（切断电机输出，类似硬件急停，不保留位置）</summary>
    public const byte CmdClose = 0x80;

    /// <summary>电机停止命令（减速停止，保留使能状态）</summary>
    public const byte CmdStopHold = 0x81;

    /// <summary>速度控制命令（data: int32 大端序，单位 0.01dps/LSB）</summary>
    public const byte CmdSpeedControl = 0xA2;

    /// <summary>多圈绝对位置控制命令（data: int64 大端序，单位 0.01°/LSB，36000=1圈）</summary>
    public const byte CmdPositionAbsolute = 0xA3;

    /// <summary>
    /// 多圈绝对位置控制命令（含最大速度限制）。
    /// data: int64 目标角度（0.01°/LSB）+ uint16 最大速度（0.01dps/LSB）
    /// </summary>
    public const byte CmdPositionAbsoluteWithSpeed = 0xA4;

    /// <summary>增量位置控制命令（data: int32 大端序增量角度，单位 0.01°/LSB）</summary>
    public const byte CmdPositionRelative = 0xA7;

    /// <summary>
    /// 增量位置控制命令（含最大速度限制）。
    /// data: int32 增量角度（0.01°/LSB）+ uint16 最大速度（0.01dps/LSB）
    /// </summary>
    public const byte CmdPositionRelativeWithSpeed = 0xA8;

    /// <summary>查询状态响应帧长度（字节数）</summary>
    public const int QueryResponseLength = 13;

    /// <summary>
    /// 构造「查询电机状态」请求帧（CMD 0x9A）
    /// </summary>
    /// <param name="slaveId">从机地址（1~247）</param>
    /// <returns>4字节请求帧</returns>
    public static byte[] BuildQueryStatusFrame(byte slaveId)
    {
        // 请求帧：[3e] [9a] [slaveId] [chk]
        // 校验和 = (0x9a + slaveId + 0x7E) & 0xFF
        byte chk = ComputeChecksum([CmdQueryStatus, slaveId]);
        return [FrameHeader, CmdQueryStatus, slaveId, chk];
    }

    /// <summary>
    /// 构造命令帧。
    /// 帧格式：[3E][CMD][ID][DataLen][CMD_SUM][Data...][DATA_SUM]
    /// CMD_SUM  = ~(CMD + ID + DataLen) &amp; 0xFF（已由帧 3E 88 01 00 76 8C 验证）
    /// DATA_SUM = ~(CMD_SUM + Σdata)   &amp; 0xFF（推导值，建议首次上机实测后确认）
    /// </summary>
    /// <param name="slaveId">从机地址</param>
    /// <param name="cmd">命令字</param>
    /// <param name="data">命令参数数据（不含帧头和校验字节），null 表示无数据</param>
    /// <returns>完整命令帧</returns>
    public static byte[] BuildCommandFrame(byte slaveId, byte cmd, byte[]? data = null)
    {
        data ??= [];
        byte dataLen = (byte)data.Length;
        // CMD_SUM：对 [CMD, ID, DataLen] 的累加和取反
        byte cmdSum = (byte)(~(cmd + slaveId + dataLen) & 0xFF);
        // DATA_SUM：对 CMD_SUM 和所有数据字节的累加和取反
        // TODO: 首次上机时用串口监视验证此算法，如不匹配根据实测修正
        int accumulator = cmdSum;
        foreach (byte b in data)
        {
            accumulator += b;
        }
        byte dataSum = (byte)(~accumulator & 0xFF);
        return [FrameHeader, cmd, slaveId, dataLen, cmdSum, .. data, dataSum];
    }

    /// <summary>
    /// 构造绝对位置控制帧（CMD 0xA4）。
    /// data: [int64 目标角度 大端序，0.01°/LSB] + [uint16 最大速度 大端序，0.01dps/LSB]
    /// </summary>
    /// <param name="slaveId">从机地址</param>
    /// <param name="angleCentideg">目标角度（0.01°单位，36000=1圈）</param>
    /// <param name="maxSpeedCentidps">最大速度（0.01dps单位，60000=100dps≈16.7rpm）</param>
    public static byte[] BuildPositionAbsoluteFrame(
        byte slaveId,
        long angleCentideg,
        uint maxSpeedCentidps
    )
    {
        byte[] data =
        [
            (byte)((angleCentideg >> 56) & 0xFF),
            (byte)((angleCentideg >> 48) & 0xFF),
            (byte)((angleCentideg >> 40) & 0xFF),
            (byte)((angleCentideg >> 32) & 0xFF),
            (byte)((angleCentideg >> 24) & 0xFF),
            (byte)((angleCentideg >> 16) & 0xFF),
            (byte)((angleCentideg >> 8) & 0xFF),
            (byte)(angleCentideg & 0xFF),
            (byte)((maxSpeedCentidps >> 8) & 0xFF),
            (byte)(maxSpeedCentidps & 0xFF),
        ];
        return BuildCommandFrame(slaveId, CmdPositionAbsoluteWithSpeed, data);
    }

    /// <summary>
    /// 构造增量位置控制帧（CMD 0xA8）。
    /// data: [int32 增量角度 大端序，0.01°/LSB] + [uint16 最大速度 大端序，0.01dps/LSB]
    /// </summary>
    /// <param name="slaveId">从机地址</param>
    /// <param name="incrementCentideg">增量角度（0.01°单位，正数正转，负数反转）</param>
    /// <param name="maxSpeedCentidps">最大速度（0.01dps单位）</param>
    public static byte[] BuildPositionRelativeFrame(
        byte slaveId,
        int incrementCentideg,
        uint maxSpeedCentidps
    )
    {
        byte[] data =
        [
            (byte)((incrementCentideg >> 24) & 0xFF),
            (byte)((incrementCentideg >> 16) & 0xFF),
            (byte)((incrementCentideg >> 8) & 0xFF),
            (byte)(incrementCentideg & 0xFF),
            (byte)((maxSpeedCentidps >> 8) & 0xFF),
            (byte)(maxSpeedCentidps & 0xFF),
        ];
        return BuildCommandFrame(slaveId, CmdPositionRelativeWithSpeed, data);
    }

    /// <summary>
    /// 构造速度控制帧（CMD 0xA2）。
    /// data: [int32 速度 大端序，0.01dps/LSB，正数正转，负数反转]
    /// </summary>
    /// <param name="slaveId">从机地址</param>
    /// <param name="speedCentidps">目标速度（0.01dps单位，60000=100dps≈16.7rpm）</param>
    public static byte[] BuildSpeedControlFrame(byte slaveId, int speedCentidps)
    {
        byte[] data =
        [
            (byte)((speedCentidps >> 24) & 0xFF),
            (byte)((speedCentidps >> 16) & 0xFF),
            (byte)((speedCentidps >> 8) & 0xFF),
            (byte)(speedCentidps & 0xFF),
        ];
        return BuildCommandFrame(slaveId, CmdSpeedControl, data);
    }

    /// <summary>
    /// 解析「查询状态」响应帧，提取电机状态信息
    /// </summary>
    /// <param name="frame">完整响应帧（13字节）</param>
    /// <param name="slaveId">期望的从机地址，用于验证</param>
    /// <returns>解析出的帧数据结构</returns>
    /// <exception cref="InvalidDataException">帧格式或校验错误时抛出</exception>
    public static KtechStatusFrame ParseQueryStatusResponse(ReadOnlySpan<byte> frame, byte slaveId)
    {
        if (frame.Length < QueryResponseLength)
        {
            throw new InvalidDataException(
                $"KTECH响应帧长度不足，期望 {QueryResponseLength} 字节，实际 {frame.Length} 字节"
            );
        }

        if (frame[0] != FrameHeader)
        {
            throw new InvalidDataException(
                $"KTECH帧头错误，期望 0x{FrameHeader:X2}，实际 0x{frame[0]:X2}"
            );
        }

        if (frame[1] != CmdQueryStatus)
        {
            throw new InvalidDataException(
                $"KTECH命令字不匹配，期望 0x{CmdQueryStatus:X2}，实际 0x{frame[1]:X2}"
            );
        }

        if (frame[2] != slaveId)
        {
            throw new InvalidDataException($"从机地址不匹配，期望 {slaveId}，实际 {frame[2]}");
        }

        // 校验和验证：计算 bytes[1..11] 的校验和
        byte computedChk = ComputeChecksum(frame[1..12]);
        if (frame[12] != computedChk)
        {
            throw new InvalidDataException(
                $"KTECH校验和错误，期望 0x{computedChk:X2}，实际 0x{frame[12]:X2}，"
                    + $"帧数据: {Convert.ToHexString(frame)}"
            );
        }

        return new KtechStatusFrame
        {
            SlaveId = frame[2],
            // 字节3~4：速度值（大端序uint16）
            SpeedRaw = (ushort)((frame[3] << 8) | frame[4]),
            // 字节5~6：位置值（大端序uint16）
            PositionRaw = (ushort)((frame[5] << 8) | frame[6]),
            // 字节7：状态标志字节
            StatusFlags = frame[7],
            // 字节8~9：扩展数据
            ExtData1 = (ushort)((frame[8] << 8) | frame[9]),
            // 字节10~11：扩展数据2
            ExtData2 = (ushort)((frame[10] << 8) | frame[11]),
        };
    }

    /// <summary>
    /// 计算 KTECH 帧校验和
    /// </summary>
    /// <param name="data">参与校验的字节序列（从CMD字节起到最后数据字节）</param>
    /// <returns>校验和字节</returns>
    /// <remarks>
    /// 经已知帧数据推导：CHK = (Σ data) &amp; 0xFF ^ 0x82
    /// 两帧验证：
    ///   Motor1: sum=0x21E → 0x1E ^ 0x82 = 0x9C ✓
    ///   Motor2: sum=0x243 → 0x43 ^ 0x82 = 0xC1 ✓
    /// </remarks>
    public static byte ComputeChecksum(ReadOnlySpan<byte> data)
    {
        int sum = 0;
        foreach (byte b in data)
        {
            sum += b;
        }

        return (byte)((sum & 0xFF) ^ 0x82);
    }
}

/// <summary>
/// KTECH CMD 0x9A 查询响应帧解析结果
/// </summary>
public class KtechStatusFrame
{
    /// <summary>从机地址</summary>
    public byte SlaveId { get; init; }

    /// <summary>
    /// 原始速度值（字节3~4，大端序，单位待官方文档确认，可能为 RPM 或 pulse/s）
    /// </summary>
    public ushort SpeedRaw { get; init; }

    /// <summary>
    /// 原始位置值（字节5~6，大端序，单位待官方文档确认，可能为 pulse）
    /// </summary>
    public ushort PositionRaw { get; init; }

    /// <summary>
    /// 状态标志字节（字节7）：
    /// bit0 - 使能状态（1=使能）
    /// bit3 - 运动中（1=运动中）
    /// 其余位含义待官方文档确认
    /// </summary>
    public byte StatusFlags { get; init; }

    /// <summary>扩展数据1（字节8~9）</summary>
    public ushort ExtData1 { get; init; }

    /// <summary>扩展数据2（字节10~11）</summary>
    public ushort ExtData2 { get; init; }

    /// <summary>根据 StatusFlags 判断电机是否已使能</summary>
    public bool IsEnabled => (StatusFlags & 0x01) != 0;

    /// <summary>根据 StatusFlags 判断电机是否正在运动</summary>
    public bool IsMoving => (StatusFlags & 0x08) != 0;

    /// <summary>根据 StatusFlags 判断是否存在故障</summary>
    public bool HasFault => (StatusFlags & 0x40) != 0;
}
