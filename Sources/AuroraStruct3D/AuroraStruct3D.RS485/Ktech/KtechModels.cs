namespace AuroraStruct3D.RS485.Ktech;

/// <summary>
/// 产品信息（CMD 0x12 响应 58 字节）
/// </summary>
public class KtechProductInfo
{
    /// <summary>驱动器名称（20 字节字符串，去除尾部空字符）</summary>
    public string DriverName { get; init; } = string.Empty;

    /// <summary>电机名称（20 字节字符串）</summary>
    public string MotorName { get; init; } = string.Empty;

    /// <summary>芯片 ID（12 字节，十六进制字符串）</summary>
    public string ChipId { get; init; } = string.Empty;

    /// <summary>硬件版本（VX.X）</summary>
    public string HardwareVersion { get; init; } = string.Empty;

    /// <summary>电机固件版本（VX.X）</summary>
    public string MotorVersion { get; init; } = string.Empty;

    /// <summary>驱动器固件版本（VX.X）</summary>
    public string FirmwareVersion { get; init; } = string.Empty;
}

/// <summary>
/// 设置参数结构（对应 Demo saveSetting_t，wire size = 104 字节带 Pack=8 对齐填充）
/// </summary>
/// <remarks>
/// 字段顺序、类型、对齐方式与 Demo C# 结构严格一致；
/// 序列化时按 Phase 0 探针 (Tools/KtechStructSizeProbe) 报告的偏移逐字段写入小端。
/// </remarks>
public class KtechSetting
{
    /// <summary>驱动 ID（offset 0）</summary>
    public byte DriverId { get; set; }

    /// <summary>总线类型：0=无, 1=RS485, 2=CAN（offset 1）</summary>
    public byte BusType { get; set; }

    /// <summary>RS485 波特率索引（offset 2）</summary>
    public byte Rs485BaudRate { get; set; }

    /// <summary>CAN 波特率索引（offset 3）</summary>
    public byte CanBaudRate { get; set; }

    /// <summary>广播模式（offset 4）</summary>
    public byte BroadcastMode { get; set; }

    /// <summary>旋转方向：0=Normal, 1=Reverse（offset 5）</summary>
    public byte SpinDirection { get; set; }

    /// <summary>电机温度保护启用（offset 6）</summary>
    public byte ProtectMotorTempEnable { get; set; }

    /// <summary>驱动温度保护启用（offset 7）</summary>
    public byte ProtectDriverTempEnable { get; set; }

    /// <summary>欠压保护启用（offset 8）</summary>
    public byte ProtectUnderVoltageEnable { get; set; }

    /// <summary>过压保护启用（offset 9）</summary>
    public byte ProtectOverVoltageEnable { get; set; }

    /// <summary>过流保护启用（offset 10）</summary>
    public byte ProtectOverCurrentEnable { get; set; }

    /// <summary>短路保护启用（offset 11）</summary>
    public byte ProtectShortCircuitEnable { get; set; }

    /// <summary>失速保护启用（offset 12）</summary>
    public byte ProtectStallEnable { get; set; }

    /// <summary>失控保护启用（offset 13）</summary>
    public byte ProtectLostInputEnable { get; set; }

    /// <summary>电机温度阈值（℃，u8，offset 14）</summary>
    public byte ProtectMotorTemp { get; set; }

    /// <summary>驱动温度阈值（℃，u8，offset 15）</summary>
    public byte ProtectDriverTemp { get; set; }

    /// <summary>欠压阈值（0.01V/LSB，offset 16）</summary>
    public ushort ProtectUnderVoltage { get; set; }

    /// <summary>过压阈值（0.01V/LSB，offset 18）</summary>
    public ushort ProtectOverVoltage { get; set; }

    /// <summary>过流阈值（mA，offset 20）</summary>
    public ushort ProtectOverCurrent { get; set; }

    /// <summary>过流响应时间（ms，offset 22）</summary>
    public ushort ProtectOverCurrentTime { get; set; }

    /// <summary>失速响应时间（ms，offset 24）</summary>
    public ushort ProtectStallTime { get; set; }

    /// <summary>失控响应时间（ms，offset 26）</summary>
    public ushort ProtectLostInputTime { get; set; }

    /// <summary>制动电阻启用（offset 28，后跟 1B 填充）</summary>
    public byte BrakeResEnable { get; set; }

    /// <summary>制动电阻开启电压（0.01V/LSB，offset 30）</summary>
    public ushort BrakeResOnVoltage { get; set; }

    /// <summary>输入类型（offset 32）</summary>
    public byte InputType { get; set; }

    /// <summary>PWM 输入控制模式（offset 33）</summary>
    public byte PwmInputControlMode { get; set; }

    /// <summary>PWM 输入最小值（offset 34）</summary>
    public ushort PwmInputMinValue { get; set; }

    /// <summary>PWM 输入最大值（offset 36）</summary>
    public ushort PwmInputMaxValue { get; set; }

    /// <summary>PWM 输入中心值（offset 38）</summary>
    public ushort PwmInputCenterValue { get; set; }

    /// <summary>PWM 输入死区（offset 40）</summary>
    public ushort PwmInputDeadband { get; set; }

    /// <summary>PWM 到力矩转换比（offset 42）</summary>
    public ushort PwmToTorqueRatio { get; set; }

    /// <summary>PWM 到速度转换比（offset 44）</summary>
    public ushort PwmToSpeedRatio { get; set; }

    /// <summary>PWM 到角度转换比（offset 46）</summary>
    public ushort PwmToAngleRatio { get; set; }

    /// <summary>每圈脉冲数（offset 48）</summary>
    public ushort PulsesPerCircle { get; set; }

    /// <summary>角度 PID Kp（u16，offset 50）</summary>
    public ushort AnglePidKp { get; set; }

    /// <summary>角度 PID Ki（u16，offset 52）</summary>
    public ushort AnglePidKi { get; set; }

    /// <summary>角度 PID Kd（u16，offset 54）</summary>
    public ushort AnglePidKd { get; set; }

    /// <summary>速度 PID Kp（u16，offset 56）</summary>
    public ushort SpeedPidKp { get; set; }

    /// <summary>速度 PID Ki（u16，offset 58）</summary>
    public ushort SpeedPidKi { get; set; }

    /// <summary>速度 PID Kd（u16，offset 60）</summary>
    public ushort SpeedPidKd { get; set; }

    /// <summary>电流 PID Kp（u16，offset 62）</summary>
    public ushort CurrentPidKp { get; set; }

    /// <summary>电流 PID Ki（u16，offset 64）</summary>
    public ushort CurrentPidKi { get; set; }

    /// <summary>电流 PID Kd（u16，offset 66）</summary>
    public ushort CurrentPidKd { get; set; }

    /// <summary>最大力矩（s16，mA；MS 型为功率 W，offset 68，后跟 2B 填充）</summary>
    public short MaxTorque { get; set; }

    /// <summary>最大速度（s32，0.01dps/LSB，offset 72，后跟 4B 填充以对齐 s64）</summary>
    public int MaxSpeed { get; set; }

    /// <summary>最大角度（s64，0.01°/LSB，offset 80）</summary>
    public long MaxAngle { get; set; }

    /// <summary>电流斜率（s16，offset 88，后跟 2B 填充）</summary>
    public short CurrentRamp { get; set; }

    /// <summary>速度斜率（s32，dps/s，offset 92）</summary>
    public int SpeedRamp { get; set; }

    /// <summary>设备唯一 ID（offset 96）</summary>
    public uint UniqueId { get; set; }

    /// <summary>保存标志（一般为 0x55555555，offset 100）</summary>
    public uint SavedFlag { get; set; }
}

/// <summary>
/// KTECH 标定数据通用接口：用于按 <see cref="KtechDeviceType"/> 多态返回 MS/MF/MH 或 MG/MG_E 结构
/// </summary>
public interface IKtechCalib
{
    /// <summary>实际设备型号（用于 Encode 时反向分发）</summary>
    KtechDeviceType DeviceType { get; }

    /// <summary>电机极对数</summary>
    byte MotorPoles { get; set; }

    /// <summary>编码器类型索引</summary>
    byte EncoderType { get; set; }

    /// <summary>编码器位置：0=Normal, 1=Reverse</summary>
    byte EncoderPos { get; set; }

    /// <summary>电机相序：0=Normal, 1=Reverse</summary>
    byte MotorPhaseSequence { get; set; }

    /// <summary>电机编码器对齐偏置（u32）</summary>
    uint MotorEncoderAlignBias { get; set; }

    /// <summary>电机编码器对齐比率（u16）</summary>
    ushort MotorEncoderAlignRatio { get; set; }

    /// <summary>电机编码器对齐电压（0.01V/LSB）</summary>
    ushort MotorEncoderAlignVoltage { get; set; }

    /// <summary>电机编码器对齐标志</summary>
    byte MotorEncoderAlignFlag { get; set; }

    /// <summary>编码器偏置（u32）</summary>
    uint EncoderOffset { get; set; }

    /// <summary>编码器偏置标志</summary>
    byte EncoderOffsetFlag { get; set; }

    /// <summary>保存标志（一般为 0x55555555）</summary>
    uint SavedFlag { get; set; }
}

/// <summary>
/// 标定参数结构（MS/MF/MH 型，对应 Demo saveCalibMsMfMh_struct，wire size = 28 字节）
/// </summary>
/// <remarks>
/// 布局（Pack=8 对齐）：
/// 0..3 byte×4 | 4..7 u32 alignBias | 8..9 u16 alignRatio | 10..11 u16 alignVoltage |
/// 12 byte alignFlag | 13..15 PADDING | 16..19 u32 encoderOffset | 20 byte encoderOffsetFlag |
/// 21..23 PADDING | 24..27 u32 savedFlag
/// </remarks>
public class KtechCalib : IKtechCalib
{
    /// <inheritdoc/>
    public KtechDeviceType DeviceType { get; init; } = KtechDeviceType.Ms;

    /// <inheritdoc/>
    public byte MotorPoles { get; set; }

    /// <inheritdoc/>
    public byte EncoderType { get; set; }

    /// <inheritdoc/>
    public byte EncoderPos { get; set; }

    /// <inheritdoc/>
    public byte MotorPhaseSequence { get; set; }

    /// <inheritdoc/>
    public uint MotorEncoderAlignBias { get; set; }

    /// <inheritdoc/>
    public ushort MotorEncoderAlignRatio { get; set; }

    /// <inheritdoc/>
    public ushort MotorEncoderAlignVoltage { get; set; }

    /// <inheritdoc/>
    public byte MotorEncoderAlignFlag { get; set; }

    /// <inheritdoc/>
    public uint EncoderOffset { get; set; }

    /// <inheritdoc/>
    public byte EncoderOffsetFlag { get; set; }

    /// <inheritdoc/>
    public uint SavedFlag { get; set; }
}

/// <summary>
/// 标定参数结构（MG/MG_E 型，对应 Demo saveCalibMg_struct，wire size = 108 字节）
/// </summary>
/// <remarks>
/// 布局（Pack=8 对齐，关键填充偏移 13/78/86/93/101）：
/// 0..3 byte×4 | 4..7 u32 alignBias | 8..9 u16 alignRatio | 10..11 u16 alignVoltage |
/// 12 byte alignFlag | 13 PADDING | 14..77 u16×32 alignValueList |
/// 78..79 PADDING | 80..83 u32 encoderOffset | 84 byte encoderOffsetFlag | 85 byte reductionRatio |
/// 86..87 PADDING | 88..91 u32 encoderRelateValue | 92 byte encoderRelateValueFlag |
/// 93..95 PADDING | 96..99 u32 encoder2_offset | 100 byte encoder2_offsetFlag |
/// 101..103 PADDING | 104..107 u32 savedFlag
/// </remarks>
public class KtechCalibMg : IKtechCalib
{
    /// <inheritdoc/>
    public KtechDeviceType DeviceType { get; init; } = KtechDeviceType.Mg;

    /// <inheritdoc/>
    public byte MotorPoles { get; set; }

    /// <inheritdoc/>
    public byte EncoderType { get; set; }

    /// <inheritdoc/>
    public byte EncoderPos { get; set; }

    /// <inheritdoc/>
    public byte MotorPhaseSequence { get; set; }

    /// <inheritdoc/>
    public uint MotorEncoderAlignBias { get; set; }

    /// <inheritdoc/>
    public ushort MotorEncoderAlignRatio { get; set; }

    /// <inheritdoc/>
    public ushort MotorEncoderAlignVoltage { get; set; }

    /// <inheritdoc/>
    public byte MotorEncoderAlignFlag { get; set; }

    /// <summary>对齐校准值数组（32 个 u16，offset 14..77）</summary>
    public ushort[] AlignValueList { get; set; } = new ushort[32];

    /// <inheritdoc/>
    public uint EncoderOffset { get; set; }

    /// <inheritdoc/>
    public byte EncoderOffsetFlag { get; set; }

    /// <summary>减速比（offset 85，仅 MG_E 有效）</summary>
    public byte ReductionRatio { get; set; }

    /// <summary>编码器关联值（u32，offset 88）</summary>
    public uint EncoderRelateValue { get; set; }

    /// <summary>编码器关联值标志（offset 92）</summary>
    public byte EncoderRelateValueFlag { get; set; }

    /// <summary>第二编码器偏置（u32，offset 96，MG_E 专用）</summary>
    public uint Encoder2Offset { get; set; }

    /// <summary>第二编码器偏置标志（offset 100）</summary>
    public byte Encoder2OffsetFlag { get; set; }

    /// <inheritdoc/>
    public uint SavedFlag { get; set; }
}

/// <summary>
/// 状态 1 + 错误位（CMD 0x9A 响应 7 字节）
/// </summary>
public class KtechState1
{
    /// <summary>电机温度（℃，s8）</summary>
    public sbyte MotorTemperature { get; init; }

    /// <summary>母线电压（V，s16/100）</summary>
    public double BusVoltage { get; init; }

    /// <summary>母线电流（A，s16/100）</summary>
    public double BusCurrent { get; init; }

    /// <summary>原始电压字节（用于调试）</summary>
    public short RawVoltage { get; init; }

    /// <summary>原始电流字节（用于调试）</summary>
    public short RawCurrent { get; init; }

    /// <summary>错误标志位</summary>
    public KtechErrorFlags ErrorFlags { get; init; }
}

/// <summary>
/// 状态 2（CMD 0x9C 响应 7 字节）
/// </summary>
public class KtechState2
{
    /// <summary>电机温度（℃）</summary>
    public sbyte MotorTemperature { get; init; }

    /// <summary>力矩/功率（s16，MS 型为功率 W，其他型为 mA）</summary>
    public short TorqueOrPower { get; init; }

    /// <summary>速度（s16，dps）</summary>
    public short Speed { get; init; }

    /// <summary>编码器原始值（u16）</summary>
    public ushort EncoderValue { get; init; }
}

/// <summary>
/// 状态 3（CMD 0x9D 响应 7 字节）
/// </summary>
public class KtechState3
{
    /// <summary>电机温度（℃）</summary>
    public sbyte MotorTemperature { get; init; }

    /// <summary>A 相电流（mA）</summary>
    public short Ia { get; init; }

    /// <summary>B 相电流（mA）</summary>
    public short Ib { get; init; }

    /// <summary>C 相电流（mA）</summary>
    public short Ic { get; init; }
}

/// <summary>
/// 运动控制响应（多数运动命令返回 7 字节，结构同 State2）
/// </summary>
public class KtechMotionResponse
{
    /// <summary>电机温度</summary>
    public sbyte MotorTemperature { get; init; }

    /// <summary>力矩/功率</summary>
    public short TorqueOrPower { get; init; }

    /// <summary>速度</summary>
    public short Speed { get; init; }

    /// <summary>编码器值</summary>
    public ushort EncoderValue { get; init; }
}
