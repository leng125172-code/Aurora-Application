namespace AuroraStruct3D.Ktech.Dtos;

/// <summary>
/// 瓴控 KTECH 实时状态快照 DTO，由 Sampler 周期采集后通过 SignalR 推送。
/// </summary>
public class KtechStateSnapshotDto
{
    /// <summary>电机轴 ID</summary>
    public Guid AxisId { get; set; }

    /// <summary>RS485 从机地址</summary>
    public int SlaveId { get; set; }

    /// <summary>采样时间戳（UTC，毫秒）</summary>
    public long TimestampMs { get; set; }

    /// <summary>采样耗时（毫秒）</summary>
    public int ElapsedMs { get; set; }

    /// <summary>本次采样是否成功（任意子读取失败置 false）</summary>
    public bool IsSuccess { get; set; }

    /// <summary>失败原因（成功时为空）</summary>
    public string? FailureReason { get; set; }

    // ===== State1（CMD 0x9A）=====
    /// <summary>电机温度（℃）</summary>
    public sbyte MotorTemperature { get; set; }

    /// <summary>母线电压（V）</summary>
    public double BusVoltage { get; set; }

    /// <summary>母线电流（A）</summary>
    public double BusCurrent { get; set; }

    /// <summary>错误标志位</summary>
    public KtechErrorFlagsDto ErrorFlags { get; set; } = new();

    // ===== State2（CMD 0x9C）=====
    /// <summary>力矩（mA；MS 型为功率 W）</summary>
    public short TorqueOrPower { get; set; }

    /// <summary>速度（dps）</summary>
    public short Speed { get; set; }

    /// <summary>编码器原始值（u16）</summary>
    public ushort EncoderValue { get; set; }

    // ===== 角度 =====
    /// <summary>多圈角度（°）</summary>
    public double MultiTurnAngle { get; set; }

    /// <summary>单圈角度（0.01°单位，0..35999；CMD 0x94 读取并归一化）</summary>
    public uint SingleTurnAngleCentideg { get; set; }
}
