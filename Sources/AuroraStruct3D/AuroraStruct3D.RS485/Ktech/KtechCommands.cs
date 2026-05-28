namespace AuroraStruct3D.RS485.Ktech;

/// <summary>
/// 瓴控 KTECH 私有协议命令码枚举
/// </summary>
/// <remarks>
/// 与 <c>Documents/瓴控Demo/serovMotor/cmdClass.cs</c> 一一对应。
/// 命令码使用 byte 类型在协议帧中传输。
/// </remarks>
public enum KtechCommands : byte
{
    /// <summary>重启设备</summary>
    RebootDevice = 0x07,

    /// <summary>建立连接</summary>
    Connect = 0x10,

    /// <summary>断开连接</summary>
    Disconnect = 0x11,

    /// <summary>读产品信息（返回 58 字节：驱动名/电机名/芯片ID/3 版本号）</summary>
    ReadInfo = 0x12,

    /// <summary>读设置（返回 saveSetting_t 结构）</summary>
    ReadSetting = 0x14,

    /// <summary>写设置到 RAM（入参 saveSetting_t 结构）</summary>
    WriteSetting = 0x15,

    /// <summary>读标定（返回 saveCalibMg_struct 或 saveCalibMsMfMh_struct）</summary>
    ReadCalib = 0x16,

    /// <summary>写标定（入参对应结构）</summary>
    WriteCalib = 0x17,

    /// <summary>执行电机编码器对齐（返回 7 字节，含相序/对齐比率）</summary>
    Calibrate = 0x18,

    /// <summary>设置编码器零点偏置（返回 7 字节，含偏置值）</summary>
    SetOffset = 0x19,

    /// <summary>读设备类型（返回 2 字节 uint16 小端）</summary>
    ReadDeviceType = 0x1F,

    /// <summary>减速器电机对齐（MG_E 专用，返回完整 saveCalibMg 结构）</summary>
    ReducerMotorAlign = 0x20,

    /// <summary>写 PID 参数到 RAM（7 字节入参）</summary>
    WritePidRam = 0x31,

    /// <summary>通用参数写 RAM（7 字节入参，返回 2 字节）</summary>
    WriteParameterToRam = 0x42,

    /// <summary>写参数到 ROM 持久化</summary>
    WriteParameterToRom = 0x44,

    /// <summary>重置设置参数为出厂值</summary>
    ResetSettingParameter = 0x48,

    /// <summary>重置标定参数</summary>
    ResetCalibParameter = 0x4A,

    /// <summary>电机关闭（切断输出，类似硬件急停）</summary>
    MotorOff = 0x80,

    /// <summary>电机停止（减速停止，保留使能）</summary>
    MotorStop = 0x81,

    /// <summary>电机开启</summary>
    MotorOn = 0x88,

    /// <summary>电机恢复</summary>
    MotorRestore = 0x89,

    /// <summary>制动控制（1 字节入参：0=制动，1=释放）</summary>
    OffBrakeControl = 0x8C,

    /// <summary>读多圈角度（返回 int64 8 字节，单位 0.01°）</summary>
    ReadMotorAngle = 0x92,

    /// <summary>清除电机圈数</summary>
    ClearMotorLoops = 0x93,

    /// <summary>读单圈角度（返回 uint32 4 字节，单位 0.01°）</summary>
    ReadMotorSingleAngle = 0x94,

    /// <summary>设置电机零点到 RAM（以当前位置为零点）</summary>
    SetMotorZeroRam = 0x95,

    /// <summary>读电机状态 1 + 错误位（返回 7 字节：温度+电压+电流+错误标志）</summary>
    ReadMotorState1Error = 0x9A,

    /// <summary>清除电机错误标志（返回 7 字节，同 State1 结构）</summary>
    ClearMotorState1Error = 0x9B,

    /// <summary>读电机状态 2（返回 7 字节：温度+力矩+速度+编码器值）</summary>
    ReadMotorState2 = 0x9C,

    /// <summary>读电机状态 3（返回 7 字节：温度+三相电流 Ia/Ib/Ic）</summary>
    ReadMotorState3 = 0x9D,

    /// <summary>开环控制（2 字节入参：电压）</summary>
    OpenControl = 0xA0,

    /// <summary>力矩控制（2 字节入参；MS 型为功率 W）</summary>
    TorqueControl = 0xA1,

    /// <summary>速度控制（4 字节入参：int32 速度 0.01dps）</summary>
    SpeedControl = 0xA2,

    /// <summary>多圈绝对角度控制（8 字节入参：int64 角度 0.01°）</summary>
    AngleControl1 = 0xA3,

    /// <summary>多圈绝对角度+速度控制（8+4 字节入参：角度+最大速度）</summary>
    AngleControl2 = 0xA4,

    /// <summary>单圈角度控制（1+4 字节入参：方向+角度）</summary>
    AngleControl3 = 0xA5,

    /// <summary>单圈角度+速度控制（1+4+4 字节入参：方向+角度+速度）</summary>
    AngleControl4 = 0xA6,

    /// <summary>增量角度控制（4 字节入参：int32 增量角度）</summary>
    AngleControl5 = 0xA7,

    /// <summary>增量角度+速度控制（4+4 字节入参：增量+速度）</summary>
    AngleControl6 = 0xA8,

    /// <summary>开始 IAP 固件升级（后续走 Ymodem 协议）</summary>
    BeginIap = 0xBB,
}

/// <summary>
/// 瓴控 KTECH 设备类型枚举
/// </summary>
public enum KtechDeviceType : ushort
{
    /// <summary>未知</summary>
    Unknown = 0,

    /// <summary>MS 型：开环电机（无力矩控制，0xA1 入参为功率 W）</summary>
    Ms = 8209,

    /// <summary>MF 型：闭环基础款</summary>
    Mf = 8225,

    /// <summary>MG 型：闭环（无减速器）</summary>
    Mg = 8241,

    /// <summary>MG_E 型：闭环（带减速器）</summary>
    MgE = 8242,

    /// <summary>MH 型：高级款</summary>
    Mh = 8257,
}

/// <summary>
/// 电机错误标志位（State1 字节 6 的 8 个 bit）
/// </summary>
[Flags]
public enum KtechErrorFlags : byte
{
    /// <summary>无错误</summary>
    None = 0,

    /// <summary>欠压保护</summary>
    UnderVoltage = 0x01,

    /// <summary>过压保护</summary>
    OverVoltage = 0x02,

    /// <summary>驱动过温</summary>
    DriverOverTemperature = 0x04,

    /// <summary>电机过温</summary>
    MotorOverTemperature = 0x08,

    /// <summary>过流保护</summary>
    OverCurrent = 0x10,

    /// <summary>短路保护</summary>
    ShortCircuit = 0x20,

    /// <summary>失速保护</summary>
    Stall = 0x40,

    /// <summary>失控保护（失去输入信号）</summary>
    LostInput = 0x80,
}
