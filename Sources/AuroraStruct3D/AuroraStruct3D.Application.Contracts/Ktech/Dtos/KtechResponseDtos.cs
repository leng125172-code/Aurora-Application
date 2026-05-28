namespace AuroraStruct3D.Ktech.Dtos;

/// <summary>
/// 状态 3 DTO（CMD 0x9D：相电流）。
/// </summary>
public class KtechState3Dto
{
    /// <summary>电机温度（℃）</summary>
    public sbyte MotorTemperature { get; set; }

    /// <summary>A 相电流（mA）</summary>
    public short Ia { get; set; }

    /// <summary>B 相电流（mA）</summary>
    public short Ib { get; set; }

    /// <summary>C 相电流（mA）</summary>
    public short Ic { get; set; }
}

/// <summary>
/// 运动控制响应 DTO（多数运动命令返回 7 字节，结构同 State2）。
/// </summary>
public class KtechMotionResponseDto
{
    /// <summary>电机温度</summary>
    public sbyte MotorTemperature { get; set; }

    /// <summary>力矩/功率</summary>
    public short TorqueOrPower { get; set; }

    /// <summary>速度</summary>
    public short Speed { get; set; }

    /// <summary>编码器值</summary>
    public ushort EncoderValue { get; set; }
}
