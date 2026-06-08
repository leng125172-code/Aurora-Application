using System.ComponentModel.DataAnnotations;

namespace AuroraStruct3D.Leisai.Dtos;

/// <summary>
/// 雷赛回原方向。值直接对应寄存器 0x600A Bit0（0=反向, 1=正向）。
/// </summary>
public enum LeisaiHomingDirection
{
    /// <summary>反向（Bit0 = 0）</summary>
    Negative = 0,

    /// <summary>正向（Bit0 = 1）</summary>
    Positive = 1,
}

/// <summary>
/// 雷赛回原模式。值直接对应寄存器 0x600A Bit2（0=限位回零, 1=原点回零）。
/// </summary>
public enum LeisaiHomingMode
{
    /// <summary>限位回零（Bit2 = 0）</summary>
    Limit = 0,

    /// <summary>原点回零（Bit2 = 1）</summary>
    Origin = 1,
}

/// <summary>
/// 雷赛回原参数配置输入 DTO。
/// 由后端负责将语义字段转换为寄存器值写入驱动。
/// </summary>
public class LeisaiHomingConfigInputDto
{
    /// <summary>回原方向（对应 0x600A Bit0）。</summary>
    public LeisaiHomingDirection HomingDirection { get; set; } = LeisaiHomingDirection.Negative;

    /// <summary>回原后是否移动到指定停止位（对应 0x600A Bit1）。</summary>
    public bool MoveAfterHome { get; set; }

    /// <summary>回原模式（对应 0x600A Bit2）。</summary>
    public LeisaiHomingMode HomingMode { get; set; } = LeisaiHomingMode.Limit;

    /// <summary>是否携带 Z 信号（对应 0x600A Bit8）。</summary>
    public bool WithZSignal { get; set; }

    /// <summary>
    /// 回零停止位（脉冲数，Int32，写入 0x600D/0x600E 高低字）。
    /// 仅当 <see cref="MoveAfterHome"/> = true 时生效，范围 -2147483648 ~ 2147483647。
    /// </summary>
    [Range(-2147483648, 2147483647)]
    public int? HomeStopPosition { get; set; }

    /// <summary>
    /// 回原速度（RPM，写入 0x600F 和 0x6010，高速与低速同值）。
    /// 为 null 时不写入，保留驱动器当前值。
    /// </summary>
    public int? HomeSpeedRpm { get; set; }

    /// <summary>
    /// 回原加速度（RPM/s，写入 0x6011 和 0x6012，加速与减速同值）。
    /// 为 null 时不写入，保留驱动器当前值。
    /// </summary>
    public int? HomeAccelerationRpm { get; set; }
}

/// <summary>
/// 雷赛限位参数配置输入 DTO。
/// 由后端负责将语义字段转换为寄存器值写入驱动。
/// </summary>
public class LeisaiLimitConfigInputDto
{
    /// <summary>是否启用软限位（对应 0x6000 Bit1：1=启用，0=禁用）。</summary>
    public bool LimitEnabled { get; set; }

    /// <summary>
    /// 正向软限位（脉冲数，Int32，写入 0x6006/0x6007 高低字）。
    /// 为 null 时不写入。
    /// </summary>
    public int? PositiveSoftLimit { get; set; }

    /// <summary>
    /// 负向软限位（脉冲数，Int32，写入 0x6008/0x6009 高低字）。
    /// 为 null 时不写入。
    /// </summary>
    public int? NegativeSoftLimit { get; set; }
}

/// <summary>
/// 雷赛回原测试结果 DTO。
/// </summary>
public class LeisaiHomingTestResultDto
{
    /// <summary>回原是否在超时时间内完成。</summary>
    public bool IsCompleted { get; set; }

    /// <summary>是否因超时退出（IsCompleted=false 且 TimedOut=true 表示超时未完成）。</summary>
    public bool TimedOut { get; set; }
}
