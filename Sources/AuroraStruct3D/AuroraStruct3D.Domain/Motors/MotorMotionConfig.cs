using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Motors;

/// <summary>
/// 电机运动配置实体。
/// 存储一套完整的运动参数（PID、速度限制、加速度等），
/// 一根轴可有多套运动配置（如"快速定位"、"精密对准"），可在运行时切换。
///
/// 数据库表：AbpProMotorMotionConfig
/// </summary>
public class MotorMotionConfig : FullAuditedEntity<Guid>
{
    // ─────────────────────────── 基本信息 ───────────────────────────

    /// <summary>所属电机轴ID</summary>
    public Guid MotorAxisId { get; private set; }

    /// <summary>配置名称（如"快速模式"、"精密模式"）</summary>
    public string Name { get; private set; } = null!;

    /// <summary>配置描述</summary>
    public string? Description { get; private set; }

    /// <summary>是否为默认配置</summary>
    public bool IsDefault { get; private set; }

    // ─────────────────────────── 速度与加速度参数 ───────────────────────────

    /// <summary>默认运行速度（rpm）</summary>
    public int DefaultSpeedRpm { get; private set; }

    /// <summary>最大速度限制（rpm）。瓴控KTECH：0~600000*0.01dps；雷赛：0~6000rpm</summary>
    public int MaxSpeedRpm { get; private set; }

    /// <summary>加速时间（ms/1000rpm）</summary>
    public int AccTimeMs { get; private set; }

    /// <summary>减速时间（ms/1000rpm）</summary>
    public int DecTimeMs { get; private set; }

    /// <summary>急停减速时间（ms/1000rpm）</summary>
    public int EmergencyDecTimeMs { get; private set; }

    // ─────────────────────────── 位置参数 ───────────────────────────

    /// <summary>
    /// 单圈脉冲数（细分数/转）。
    /// 雷赛iCL-RS：对应寄存器 Pr0.00，默认 10000 P/R。
    /// 瓴控KTECH：编码器分辨率（0.01°/LSB，36000=1圈）。
    /// </summary>
    public int PulsesPerRevolution { get; private set; }

    /// <summary>跟踪误差最大允许值（脉冲数）。超过此值触发超差报警</summary>
    public int MaxTrackingError { get; private set; }

    /// <summary>到位判定位置误差窗口（脉冲数）</summary>
    public int InPositionWindow { get; private set; }

    /// <summary>到位判定消抖延时（ms）</summary>
    public int InPositionDebounceMs { get; private set; }

    // ─────────────────────────── PID 参数（雷赛iCL-RS 适用） ───────────────────────────

    /// <summary>位置环比例增益 Kp（0~3000）</summary>
    public int? PositionKp { get; private set; }

    /// <summary>速度环比例增益 Kp（0~3000）</summary>
    public int? SpeedKp { get; private set; }

    /// <summary>速度环积分增益 Ki（0~3000）</summary>
    public int? SpeedKi { get; private set; }

    // ─────────────────────────── 瓴控KTECH 专属参数 ───────────────────────────

    /// <summary>
    /// 角度限制（0.01°单位，对应瓴控 Max Angle）。
    /// 仅瓴控KTECH使用，雷赛使用软件限位寄存器。
    /// </summary>
    public long? KtechAngleLimit { get; private set; }

    /// <summary>
    /// 力矩电流限制。
    /// 瓴控MF电机：0~2000；瓴控MS电机：0~850。
    /// </summary>
    public int? KtechTorqueLimit { get; private set; }

    /// <summary>电流斜率（0~30000，对应瓴控 Current Ramp）</summary>
    public int? KtechCurrentRamp { get; private set; }

    /// <summary>速度斜率（0~600000 dps/s，对应瓴控 Speed Ramp）</summary>
    public int? KtechSpeedRamp { get; private set; }

    // ─────────────────────────── 雷赛iCL-RS 专属参数 ───────────────────────────

    /// <summary>
    /// 雷赛回零方式寄存器值（对应 Pr8.10 / 0x600A）。
    /// Bit0=回零方向，Bit2~Bit7=回零模式（0=限位，1=原点，3=力矩）。
    /// </summary>
    public int? LeisaiHomeModeReg { get; private set; }

    /// <summary>力矩回零时力矩保留时间（ms，Pr8.19）</summary>
    public int? LeisaiTorqueHomeTimeMs { get; private set; }

    /// <summary>力矩回零值（当前电流百分比，Pr8.20，0~100%）</summary>
    public int? LeisaiTorqueHomePercent { get; private set; }

    // ─────────────────────────── 构造函数 ───────────────────────────

    protected MotorMotionConfig() { }

    /// <summary>
    /// 创建运动配置
    /// </summary>
    /// <param name="id">实体ID</param>
    /// <param name="motorAxisId">所属轴ID</param>
    /// <param name="name">配置名称</param>
    public MotorMotionConfig(Guid id, Guid motorAxisId, string name)
        : base(id)
    {
        MotorAxisId = motorAxisId;
        SetName(name);
        IsDefault = false;

        // 合理的默认值
        DefaultSpeedRpm = 200;
        MaxSpeedRpm = 1000;
        AccTimeMs = 100;
        DecTimeMs = 100;
        EmergencyDecTimeMs = 10;
        PulsesPerRevolution = 10000;
        MaxTrackingError = 4000;
        InPositionWindow = 200;
        InPositionDebounceMs = 3;
    }

    // ─────────────────────────── 领域方法 ───────────────────────────

    /// <summary>设置名称</summary>
    public MotorMotionConfig SetName(string name)
    {
        Check.NotNullOrWhiteSpace(name, nameof(name), MotorConsts.MaxMotionConfigNameLength);
        Name = name;
        return this;
    }

    /// <summary>设置描述</summary>
    public MotorMotionConfig SetDescription(string? description)
    {
        if (description != null)
        {
            Check.Length(description, nameof(description), MotorConsts.MaxDescriptionLength);
        }
        Description = description;
        return this;
    }

    /// <summary>设置为默认配置</summary>
    public MotorMotionConfig SetDefault(bool isDefault)
    {
        IsDefault = isDefault;
        return this;
    }

    /// <summary>配置速度与加速度参数</summary>
    public MotorMotionConfig ConfigureSpeedAndAcceleration(
        int defaultSpeedRpm,
        int maxSpeedRpm,
        int accTimeMs,
        int decTimeMs,
        int emergencyDecTimeMs
    )
    {
        DefaultSpeedRpm = defaultSpeedRpm;
        MaxSpeedRpm = maxSpeedRpm;
        AccTimeMs = accTimeMs;
        DecTimeMs = decTimeMs;
        EmergencyDecTimeMs = emergencyDecTimeMs;
        return this;
    }

    /// <summary>配置位置精度参数</summary>
    public MotorMotionConfig ConfigurePositionAccuracy(
        int pulsesPerRevolution,
        int maxTrackingError,
        int inPositionWindow,
        int inPositionDebounceMs
    )
    {
        PulsesPerRevolution = pulsesPerRevolution;
        MaxTrackingError = maxTrackingError;
        InPositionWindow = inPositionWindow;
        InPositionDebounceMs = inPositionDebounceMs;
        return this;
    }

    /// <summary>配置PID参数（雷赛iCL-RS适用，瓴控KTECH可选）</summary>
    public MotorMotionConfig ConfigurePid(int? positionKp, int? speedKp, int? speedKi)
    {
        PositionKp = positionKp;
        SpeedKp = speedKp;
        SpeedKi = speedKi;
        return this;
    }

    /// <summary>配置瓴控KTECH专属参数</summary>
    public MotorMotionConfig ConfigureKtech(
        long? angleLimit,
        int? torqueLimit,
        int? currentRamp,
        int? speedRamp
    )
    {
        KtechAngleLimit = angleLimit;
        KtechTorqueLimit = torqueLimit;
        KtechCurrentRamp = currentRamp;
        KtechSpeedRamp = speedRamp;
        return this;
    }

    /// <summary>配置雷赛iCL-RS专属参数</summary>
    public MotorMotionConfig ConfigureLeisai(
        int? homeModeReg,
        int? torqueHomeTimeMs,
        int? torqueHomePercent
    )
    {
        LeisaiHomeModeReg = homeModeReg;
        LeisaiTorqueHomeTimeMs = torqueHomeTimeMs;
        LeisaiTorqueHomePercent = torqueHomePercent;
        return this;
    }
}
