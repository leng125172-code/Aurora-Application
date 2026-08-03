namespace AuroraStruct3D.Calibration.Dtos;

/// <summary>
/// 标定电机参数输出 DTO。
/// </summary>
public class CalibMotorParamDto
{
    /// <summary>主键。</summary>
    public Guid Id { get; set; }

    /// <summary>所属标定项目 ID。</summary>
    public Guid CalibProjectId { get; set; }

    /// <summary>电机轴 ID。</summary>
    public Guid MotorAxisId { get; set; }

    /// <summary>编码器分辨率。</summary>
    public int EncoderResolution { get; set; }

    /// <summary>减速比。</summary>
    public decimal GearRatio { get; set; }

    /// <summary>回原停止位/机械原点位置。</summary>
    public decimal MechanicalOriginPosition { get; set; }

    /// <summary>回原方向。</summary>
    public OriginDirection OriginDirection { get; set; }

    /// <summary>正向软限位。</summary>
    public decimal PositiveSoftLimit { get; set; }

    /// <summary>负向软限位。</summary>
    public decimal NegativeSoftLimit { get; set; }

    /// <summary>回原速度。</summary>
    public decimal HomeSpeed { get; set; }

    /// <summary>回原加速度。</summary>
    public decimal HomeAcceleration { get; set; }

    /// <summary>是否已锁定原点。</summary>
    public bool IsOriginLocked { get; set; }

    /// <summary>限位是否启用（雷赛 0x6000 Bit1；瓴控固定 true）。</summary>
    public bool LimitEnabled { get; set; }

    /// <summary>回原模式（限位回零 / 原点回零）。</summary>
    public CalibHomingMode HomingMode { get; set; }

    /// <summary>回原完成后是否移动到指定停止位。</summary>
    public bool MoveAfterHome { get; set; }

    /// <summary>回原时是否使用编码器 Z 信号。</summary>
    public bool WithZSignal { get; set; }

    /// <summary>创建时间。</summary>
    public DateTime CreationTime { get; set; }

    /// <summary>最后修改时间。</summary>
    public DateTime? LastModificationTime { get; set; }
}

/// <summary>
/// 保存标定电机参数输入 DTO。
/// </summary>
public class SaveCalibMotorParamInput
{
    /// <summary>所属标定项目 ID。</summary>
    public Guid CalibProjectId { get; set; }

    /// <summary>电机轴 ID。</summary>
    public Guid MotorAxisId { get; set; }

    /// <summary>编码器分辨率。</summary>
    public int? EncoderResolution { get; set; }

    /// <summary>减速比。</summary>
    public decimal? GearRatio { get; set; }

    /// <summary>回原停止位/机械原点位置。</summary>
    public decimal? MechanicalOriginPosition { get; set; }

    /// <summary>回原方向。</summary>
    public OriginDirection? OriginDirection { get; set; }

    /// <summary>正向软限位。</summary>
    public decimal? PositiveSoftLimit { get; set; }

    /// <summary>负向软限位。</summary>
    public decimal? NegativeSoftLimit { get; set; }

    /// <summary>回原速度。</summary>
    public decimal? HomeSpeed { get; set; }

    /// <summary>回原加速度。</summary>
    public decimal? HomeAcceleration { get; set; }

    /// <summary>是否已锁定原点。</summary>
    public bool? IsOriginLocked { get; set; }

    /// <summary>限位是否启用（雷赛 0x6000 Bit1；瓴控固定 true）。</summary>
    public bool? LimitEnabled { get; set; }

    /// <summary>回原模式（限位回零 / 原点回零）。</summary>
    public CalibHomingMode? HomingMode { get; set; }

    /// <summary>回原完成后是否移动到指定停止位。</summary>
    public bool? MoveAfterHome { get; set; }

    /// <summary>回原时是否使用编码器 Z 信号。</summary>
    public bool? WithZSignal { get; set; }
}
