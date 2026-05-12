using Volo.Abp.Domain.Entities;

namespace AuroraStruct3D.Motors;

/// <summary>
/// 雷赛iCL-RS PR路径配置实体。
/// 对应雷赛驱动器内部的位置表（PR Table），最多16段，
/// 每段定义运动类型、位置、速度、加减速和停顿时间。
///
/// 数据库表：AbpProMotorPrPath
/// </summary>
public class MotorPrPath : Entity<Guid>
{
    /// <summary>所属电机轴ID（必须是雷赛iCL-RS品牌轴）</summary>
    public Guid MotorAxisId { get; private set; }

    /// <summary>路径编号（0~15，对应驱动器内部路径索引）</summary>
    public int PathIndex { get; private set; }

    /// <summary>路径名称（显示用，如"去料位"、"归零"）</summary>
    public string Name { get; private set; } = null!;

    // ─────────────────────────── 运动模式寄存器（Pr9.x+0，对应0x6200、0x6208...） ───────────────────────────

    /// <summary>
    /// 运动类型。
    /// 0=无动作，1=位置定位，2=速度运行，3=回零
    /// 对应 Pr9.x+0 Bit0~3
    /// </summary>
    public int MotionType { get; private set; }

    /// <summary>
    /// 位置指令模式。
    /// 0=绝对，1=相对指令，2=相对电机，3=相对参考值
    /// 对应 Pr9.x+0 Bit6~7
    /// </summary>
    public int PositionMode { get; private set; }

    /// <summary>
    /// 是否启用插断（INS）。
    /// true=触发时中断并放弃当前路径，直接执行本路径。
    /// 对应 Pr9.x+0 Bit4
    /// </summary>
    public bool EnableInterrupt { get; private set; }

    /// <summary>
    /// 是否启用重叠（OVLP）。
    /// true=不等当前路径完成即连续跳转到下一路径。
    /// 对应 Pr9.x+0 Bit5
    /// </summary>
    public bool EnableOverlap { get; private set; }

    /// <summary>
    /// 是否启用路径跳转（JUMP）。
    /// 对应 Pr9.x+0 Bit14
    /// </summary>
    public bool EnableJump { get; private set; }

    /// <summary>
    /// 跳转目标路径编号（0~15）。
    /// EnableJump=true 时生效，对应 Pr9.x+0 Bit8~13
    /// </summary>
    public int JumpToPathIndex { get; private set; }

    // ─────────────────────────── 运动参数 ───────────────────────────

    /// <summary>
    /// 目标位置（脉冲数，32位有符号）。
    /// 绝对模式：绝对坐标；相对模式：增量值。
    /// 对应 Pr9.x+1（位置H）+ Pr9.x+2（位置L）
    /// </summary>
    public long TargetPosition { get; private set; }

    /// <summary>运行速度（rpm）。对应 Pr9.x+3</summary>
    public int SpeedRpm { get; private set; }

    /// <summary>加速时间（ms/1000rpm）。对应 Pr9.x+4</summary>
    public int AccTimeMs { get; private set; }

    /// <summary>减速时间（ms/1000rpm）。对应 Pr9.x+5</summary>
    public int DecTimeMs { get; private set; }

    /// <summary>路径结束后的停顿时间（ms）。对应 Pr9.x+6</summary>
    public int DwellTimeMs { get; private set; }

    // ─────────────────────────── 构造函数 ───────────────────────────

    protected MotorPrPath() { }

    /// <summary>
    /// 创建PR路径配置
    /// </summary>
    /// <param name="id">实体ID</param>
    /// <param name="motorAxisId">所属轴ID（必须为雷赛iCL-RS）</param>
    /// <param name="pathIndex">路径编号（0~15）</param>
    /// <param name="name">路径名称</param>
    public MotorPrPath(Guid id, Guid motorAxisId, int pathIndex, string name)
        : base(id)
    {
        MotorAxisId = motorAxisId;

        if (pathIndex is < 0 or > 15)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pathIndex),
                "PR路径编号必须在 0~15 范围内"
            );
        }
        PathIndex = pathIndex;

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("路径名称不能为空", nameof(name));
        }
        Name = name[..Math.Min(name.Length, MotorConsts.MaxPrPathNameLength)];

        // 默认：绝对位置定位，无插断，无重叠，无跳转
        MotionType = 1;
        PositionMode = 0;
        EnableInterrupt = false;
        EnableOverlap = false;
        EnableJump = false;
        JumpToPathIndex = 0;
        SpeedRpm = 200;
        AccTimeMs = 100;
        DecTimeMs = 100;
        DwellTimeMs = 0;
    }

    // ─────────────────────────── 领域方法 ───────────────────────────

    /// <summary>
    /// 配置定位路径（位置定位模式）
    /// </summary>
    public MotorPrPath ConfigurePositioning(
        long targetPosition,
        int speedRpm,
        int accTimeMs,
        int decTimeMs,
        int dwellTimeMs = 0,
        int positionMode = 0
    )
    {
        MotionType = 1;
        TargetPosition = targetPosition;
        SpeedRpm = speedRpm;
        AccTimeMs = accTimeMs;
        DecTimeMs = decTimeMs;
        DwellTimeMs = dwellTimeMs;
        PositionMode = positionMode;
        return this;
    }

    /// <summary>
    /// 配置速度运行模式
    /// </summary>
    public MotorPrPath ConfigureSpeedMode(int speedRpm, int accTimeMs, int decTimeMs)
    {
        MotionType = 2;
        SpeedRpm = speedRpm;
        AccTimeMs = accTimeMs;
        DecTimeMs = decTimeMs;
        return this;
    }

    /// <summary>
    /// 配置回零模式
    /// </summary>
    public MotorPrPath ConfigureHoming(int speedRpm, int accTimeMs, int decTimeMs)
    {
        MotionType = 3;
        SpeedRpm = speedRpm;
        AccTimeMs = accTimeMs;
        DecTimeMs = decTimeMs;
        return this;
    }

    /// <summary>
    /// 配置跳转行为
    /// </summary>
    public MotorPrPath ConfigureJump(
        bool enableJump,
        int jumpToPathIndex = 0,
        bool enableInterrupt = false,
        bool enableOverlap = false
    )
    {
        EnableJump = enableJump;
        JumpToPathIndex = enableJump ? jumpToPathIndex : 0;
        EnableInterrupt = enableInterrupt;
        EnableOverlap = enableOverlap;
        return this;
    }

    /// <summary>
    /// 将本路径配置转换为驱动器寄存器值（Pr9.x+0 运动模式字）
    /// </summary>
    public int ToModeRegisterValue()
    {
        int value = MotionType & 0x0F; // Bit0~3: TYPE
        if (EnableInterrupt)
        {
            value |= 0x10; // Bit4: INS
        }
        if (EnableOverlap)
        {
            value |= 0x20; // Bit5: OVLP
        }
        value |= (PositionMode & 0x03) << 6; // Bit6~7: 位置模式
        value |= (JumpToPathIndex & 0x3F) << 8; // Bit8~13: 跳转目标
        if (EnableJump)
        {
            value |= 0x4000; // Bit14: JUMP
        }
        return value;
    }
}
