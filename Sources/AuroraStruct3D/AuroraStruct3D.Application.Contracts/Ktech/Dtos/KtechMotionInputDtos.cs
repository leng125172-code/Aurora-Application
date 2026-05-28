using System.ComponentModel.DataAnnotations;

namespace AuroraStruct3D.Ktech.Dtos;

/// <summary>
/// 8 种运动模式枚举（与 KTECH 命令码一一对应）。
/// </summary>
public enum KtechMotionMode
{
    /// <summary>开环（CMD 0xA0，s16 电压）</summary>
    Open = 0,

    /// <summary>力矩/功率（CMD 0xA1，s16；MS 型为功率 W，其它为电流 mA）</summary>
    TorqueOrPower = 1,

    /// <summary>速度（CMD 0xA2，s32，0.01dps/LSB）</summary>
    Speed = 2,

    /// <summary>多圈绝对角度（CMD 0xA3，s64，0.01°/LSB）</summary>
    MultiAngle = 3,

    /// <summary>多圈绝对角度+速度（CMD 0xA4，s64+u32）</summary>
    MultiAngleWithSpeed = 4,

    /// <summary>单圈角度（CMD 0xA5，u8 方向+u32 角度）</summary>
    SingleAngle = 5,

    /// <summary>单圈角度+速度（CMD 0xA6）</summary>
    SingleAngleWithSpeed = 6,

    /// <summary>增量角度（CMD 0xA7，s32，0.01°/LSB）</summary>
    IncrementAngle = 7,

    /// <summary>增量角度+速度（CMD 0xA8，s32+u32）</summary>
    IncrementAngleWithSpeed = 8,
}

/// <summary>
/// 通用运动控制入参（前端根据 Mode 填充对应字段）。
/// </summary>
public class KtechMotionInputDto
{
    /// <summary>运动模式</summary>
    [Required]
    public KtechMotionMode Mode { get; set; }

    /// <summary>开环电压（s16，Mode=Open 时有效）</summary>
    public short Voltage { get; set; }

    /// <summary>力矩或功率（s16，Mode=TorqueOrPower 时有效）</summary>
    public short TorqueOrPower { get; set; }

    /// <summary>速度（dps，正值；Mode=MultiAngle/SingleAngle/IncrementWithSpeed 时有效）</summary>
    public double SpeedDps { get; set; }

    /// <summary>速度（dps，有符号；Mode=Speed 时使用，可正负）</summary>
    public double SpeedSignedDps { get; set; }

    /// <summary>多圈/增量角度（°，有符号）</summary>
    public double AngleDeg { get; set; }

    /// <summary>单圈角度（°，0~359.99）</summary>
    public double SingleAngleDeg { get; set; }

    /// <summary>单圈方向（0/1，Mode=SingleAngle/SingleAngleWithSpeed 时有效）</summary>
    public byte Direction { get; set; }
}

/// <summary>
/// 写入 RAM PID/限制参数入参（CMD 0x31、0x42）。
/// </summary>
public class KtechWritePidRamInputDto
{
    /// <summary>角度 Kp/Ki/Kd</summary>
    public byte AngleKp { get; set; }

    /// <summary>角度 Ki</summary>
    public byte AngleKi { get; set; }

    /// <summary>角度 Kd</summary>
    public byte AngleKd { get; set; }

    /// <summary>速度 Kp</summary>
    public byte SpeedKp { get; set; }

    /// <summary>速度 Ki</summary>
    public byte SpeedKi { get; set; }

    /// <summary>速度 Kd</summary>
    public byte SpeedKd { get; set; }

    /// <summary>电流 Kp</summary>
    public byte CurrentKp { get; set; }

    /// <summary>电流 Ki</summary>
    public byte CurrentKi { get; set; }

    /// <summary>电流 Kd</summary>
    public byte CurrentKd { get; set; }
}
