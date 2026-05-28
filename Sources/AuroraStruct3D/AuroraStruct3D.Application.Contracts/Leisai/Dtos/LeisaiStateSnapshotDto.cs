namespace AuroraStruct3D.Leisai.Dtos;

/// <summary>
/// 雷赛 iCL-RS 电机实时状态快照 DTO（由 Sampler 周期采集后通过 SignalR 推送）。
/// </summary>
public class LeisaiStateSnapshotDto
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

    // ===== 运行状态（0x1003 状态字） =====

    /// <summary>状态字原值（0x1003）</summary>
    public ushort StatusWord { get; set; }

    /// <summary>bit0：故障</summary>
    public bool IsFault { get; set; }

    /// <summary>bit1：已使能</summary>
    public bool IsEnabled { get; set; }

    /// <summary>bit2：运行中（电机正在执行运动）</summary>
    public bool IsRunning { get; set; }

    /// <summary>bit3：指令完成</summary>
    public bool IsCommandDone { get; set; }

    /// <summary>bit4：路径完成</summary>
    public bool IsPathDone { get; set; }

    /// <summary>bit5：回零完成</summary>
    public bool IsHomeDone { get; set; }

    // ===== 触发状态（0x6002 触发字） =====

    /// <summary>触发字原值（0x6002）</summary>
    public ushort TriggerWord { get; set; }

    /// <summary>触发模式描述（"Idle" / "Homing" / "PR0".."PR15" / "JOG" / "Unknown"）</summary>
    public string TriggerMode { get; set; } = "Idle";

    // ===== 位置（0x602A-0x602D，命令位置 / 实际位置 各 32 位） =====

    /// <summary>命令位置（0x602A 高 16 位 | 0x602B 低 16 位 → 有符号 32 位脉冲数）</summary>
    public int CommandPosition { get; set; }

    /// <summary>电机实际位置（0x602C 高 16 位 | 0x602D 低 16 位 → 有符号 32 位脉冲数）</summary>
    public int ActualPosition { get; set; }

    // ===== 当前生效速度（按四步法判定） =====

    /// <summary>当前生效速度（RPM 或 0.1 RPM，取决于来源寄存器；Phase 1 直接透传寄存器原值）</summary>
    public int EffectiveSpeed { get; set; }

    /// <summary>速度来源描述（如 "Stop" / "Homing" / "PR3" / "JOG"）</summary>
    public string SpeedSource { get; set; } = "Stop";

    // ===== 母线电压 =====

    /// <summary>母线电压（V）= 寄存器 0x0177 原值 / 10.0</summary>
    public double BusVoltageVolt { get; set; }

    // ===== IO 当前状态 =====

    /// <summary>输入 IO 位图（0x0179，bit0..bit6 对应 DI1..DI7）</summary>
    public ushort InputIoBitmap { get; set; }

    /// <summary>输出 IO 位图（0x017B，bit0..bit2 对应 DO1..DO3）</summary>
    public ushort OutputIoBitmap { get; set; }

    // ===== 故障 =====

    /// <summary>当前故障码（0x2203）</summary>
    public ushort CurrentFaultCode { get; set; }

    /// <summary>PR 路径警告码（0x601D）</summary>
    public ushort PrWarningCode { get; set; }
}
