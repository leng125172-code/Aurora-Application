namespace AuroraStruct3D.Ktech.Dtos;

/// <summary>
/// 瓴控 KTECH 设置参数 DTO（CMD 0x14 / 0x15），字段顺序/类型与 Demo saveSetting_t 严格对齐。
/// </summary>
/// <remarks>
/// 类型变更（v2，匹配 Demo C# 结构）：
///   - PID 系列由 byte 修正为 ushort（原驱动按 byte 解析为错误数值）
///   - ProtectMotorTemp / ProtectDriverTemp 由 sbyte 修正为 byte
///   - MaxTorque/CurrentRamp 改为 short；MaxSpeed/SpeedRamp 改为 int；MaxAngle 改为 long
///   - 新增 InputType / PwmInputControlMode / 8×PWM / PulsesPerCircle / UniqueId / SavedFlag
/// </remarks>
public class KtechSettingDto
{
    /// <summary>驱动 ID</summary>
    public byte DriverId { get; set; }

    /// <summary>总线类型：0=无, 1=RS485, 2=CAN</summary>
    public byte BusType { get; set; }

    /// <summary>RS485 波特率索引</summary>
    public byte Rs485BaudRate { get; set; }

    /// <summary>CAN 波特率索引</summary>
    public byte CanBaudRate { get; set; }

    /// <summary>广播模式</summary>
    public byte BroadcastMode { get; set; }

    /// <summary>旋转方向：0=Normal, 1=Reverse</summary>
    public byte SpinDirection { get; set; }

    /// <summary>电机温度保护启用</summary>
    public byte ProtectMotorTempEnable { get; set; }

    /// <summary>驱动温度保护启用</summary>
    public byte ProtectDriverTempEnable { get; set; }

    /// <summary>欠压保护启用</summary>
    public byte ProtectUnderVoltageEnable { get; set; }

    /// <summary>过压保护启用</summary>
    public byte ProtectOverVoltageEnable { get; set; }

    /// <summary>过流保护启用</summary>
    public byte ProtectOverCurrentEnable { get; set; }

    /// <summary>短路保护启用</summary>
    public byte ProtectShortCircuitEnable { get; set; }

    /// <summary>失速保护启用</summary>
    public byte ProtectStallEnable { get; set; }

    /// <summary>失控保护启用</summary>
    public byte ProtectLostInputEnable { get; set; }

    /// <summary>电机温度阈值（℃，u8）</summary>
    public byte ProtectMotorTemp { get; set; }

    /// <summary>驱动温度阈值（℃，u8）</summary>
    public byte ProtectDriverTemp { get; set; }

    /// <summary>欠压阈值（V）</summary>
    public double ProtectUnderVoltage { get; set; }

    /// <summary>过压阈值（V）</summary>
    public double ProtectOverVoltage { get; set; }

    /// <summary>过流阈值（mA）</summary>
    public ushort ProtectOverCurrent { get; set; }

    /// <summary>过流响应时间（ms）</summary>
    public ushort ProtectOverCurrentTime { get; set; }

    /// <summary>失速响应时间（ms）</summary>
    public ushort ProtectStallTime { get; set; }

    /// <summary>失控响应时间（ms）</summary>
    public ushort ProtectLostInputTime { get; set; }

    /// <summary>制动电阻启用</summary>
    public byte BrakeResEnable { get; set; }

    /// <summary>制动电阻开启电压（V）</summary>
    public double BrakeResOnVoltage { get; set; }

    /// <summary>输入类型</summary>
    public byte InputType { get; set; }

    /// <summary>PWM 输入控制模式</summary>
    public byte PwmInputControlMode { get; set; }

    /// <summary>PWM 输入最小值</summary>
    public ushort PwmInputMinValue { get; set; }

    /// <summary>PWM 输入最大值</summary>
    public ushort PwmInputMaxValue { get; set; }

    /// <summary>PWM 输入中心值</summary>
    public ushort PwmInputCenterValue { get; set; }

    /// <summary>PWM 输入死区</summary>
    public ushort PwmInputDeadband { get; set; }

    /// <summary>PWM 到力矩转换比</summary>
    public ushort PwmToTorqueRatio { get; set; }

    /// <summary>PWM 到速度转换比</summary>
    public ushort PwmToSpeedRatio { get; set; }

    /// <summary>PWM 到角度转换比</summary>
    public ushort PwmToAngleRatio { get; set; }

    /// <summary>每圈脉冲数</summary>
    public ushort PulsesPerCircle { get; set; }

    /// <summary>角度 PID Kp</summary>
    public ushort AnglePidKp { get; set; }

    /// <summary>角度 PID Ki</summary>
    public ushort AnglePidKi { get; set; }

    /// <summary>角度 PID Kd</summary>
    public ushort AnglePidKd { get; set; }

    /// <summary>速度 PID Kp</summary>
    public ushort SpeedPidKp { get; set; }

    /// <summary>速度 PID Ki</summary>
    public ushort SpeedPidKi { get; set; }

    /// <summary>速度 PID Kd</summary>
    public ushort SpeedPidKd { get; set; }

    /// <summary>电流 PID Kp</summary>
    public ushort CurrentPidKp { get; set; }

    /// <summary>电流 PID Ki</summary>
    public ushort CurrentPidKi { get; set; }

    /// <summary>电流 PID Kd</summary>
    public ushort CurrentPidKd { get; set; }

    /// <summary>最大力矩（s16，mA；MS 型为功率 W）</summary>
    public short MaxTorque { get; set; }

    /// <summary>最大速度（dps）</summary>
    public double MaxSpeed { get; set; }

    /// <summary>最大角度（°）</summary>
    public double MaxAngle { get; set; }

    /// <summary>电流斜率（s16）</summary>
    public short CurrentRamp { get; set; }

    /// <summary>速度斜率（s32，dps/s）</summary>
    public int SpeedRamp { get; set; }

    /// <summary>设备唯一 ID</summary>
    public uint UniqueId { get; set; }

    /// <summary>保存标志（一般为 0x55555555）</summary>
    public uint SavedFlag { get; set; }
}
