using Volo.Abp.Domain.Entities;

namespace AuroraStruct3D.CalibrationManagement;

/// <summary>
/// 伺服（电机）参数：标定语境下的电机配置快照。
/// 包括编码器分辨率、减速比、原点位置、软限位、回原参数等。
/// </summary>
public class CalibrationMotorParameter : Entity<Guid>
{
    /// <summary>所属标定设备 ID</summary>
    public Guid CalibrationDeviceId { get; private set; }

    /// <summary>对应物理电机轴 ID</summary>
    public Guid MotorAxisId { get; private set; }

    /// <summary>逻辑角色（冗余，便于查询）</summary>
    public MotorRole Role { get; private set; }

    /// <summary>编码器分辨率（每转脉冲数）</summary>
    public int EncoderResolution { get; private set; }

    /// <summary>减速比</summary>
    public double GearRatio { get; private set; }

    /// <summary>机械原点位置（单位与电机轴类型一致：旋转=度、平移=mm）</summary>
    public double HomePosition { get; private set; }

    /// <summary>回原方向（+1 正向 / -1 反向）</summary>
    public int HomeDirection { get; private set; }

    /// <summary>软限位下限</summary>
    public double SoftLimitMin { get; private set; }

    /// <summary>软限位上限</summary>
    public double SoftLimitMax { get; private set; }

    /// <summary>回原速度</summary>
    public double HomingVelocity { get; private set; }

    /// <summary>回原加速度</summary>
    public double HomingAcceleration { get; private set; }

    /// <summary>是否已完成回原</summary>
    public bool IsHomed { get; private set; }

    /// <summary>最近一次完成回原的时间（UTC）</summary>
    public DateTime? LastHomedTime { get; private set; }

    /// <summary>EF Core 构造函数</summary>
    protected CalibrationMotorParameter() { }

    /// <summary>创建电机参数</summary>
    public CalibrationMotorParameter(
        Guid id,
        Guid calibrationDeviceId,
        Guid motorAxisId,
        MotorRole role
    )
        : base(id)
    {
        CalibrationDeviceId = calibrationDeviceId;
        MotorAxisId = motorAxisId;
        Role = role;
    }

    /// <summary>设置机械参数（编码器分辨率、减速比）</summary>
    public void SetMechanical(int encoderResolution, double gearRatio)
    {
        EncoderResolution = encoderResolution;
        GearRatio = gearRatio;
    }

    /// <summary>设置原点与回原方向</summary>
    public void SetHoming(
        double homePosition,
        int homeDirection,
        double homingVelocity,
        double homingAcceleration
    )
    {
        HomePosition = homePosition;
        HomeDirection = homeDirection;
        HomingVelocity = homingVelocity;
        HomingAcceleration = homingAcceleration;
    }

    /// <summary>设置软限位区间</summary>
    public void SetSoftLimits(double min, double max)
    {
        SoftLimitMin = min;
        SoftLimitMax = max;
    }

    /// <summary>标记已完成回原</summary>
    public void MarkHomed()
    {
        IsHomed = true;
        LastHomedTime = DateTime.UtcNow;
    }
}
