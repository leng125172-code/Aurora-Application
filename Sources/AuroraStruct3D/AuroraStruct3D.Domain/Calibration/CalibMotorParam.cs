using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// 标定电机参数实体（Step 2）。
/// 记录某个标定项目中某根电机轴的标定专用参数配置，
/// 包括编码器分辨率、减速比、软限位、回原参数等。
///
/// 数据库表：AbpProCalibMotorParams
/// </summary>
public class CalibMotorParam : FullAuditedEntity<Guid>
{
    /// <summary>所属标定项目ID（关联AbpProCalibProjects）</summary>
    public Guid CalibProjectId { get; private set; }

    /// <summary>电机轴ID（关联AbpProMotorAxes）</summary>
    public Guid MotorAxisId { get; private set; }

    /// <summary>电机类型（旋转电机 / 距离电机）</summary>
    public CalibMotorType MotorType { get; private set; }

    /// <summary>编码器分辨率（脉冲/圈）</summary>
    public int EncoderResolution { get; private set; }

    /// <summary>减速比</summary>
    public decimal GearRatio { get; private set; }

    /// <summary>机械原点位置</summary>
    public decimal MechanicalOriginPosition { get; private set; }

    /// <summary>回原方向（正向 / 反向）</summary>
    public OriginDirection OriginDirection { get; private set; }

    /// <summary>正方向软限位（旋转电机单位：度°，距离电机单位：mm）</summary>
    public decimal PositiveSoftLimit { get; private set; }

    /// <summary>负方向软限位（旋转电机单位：度°，距离电机单位：mm）</summary>
    public decimal NegativeSoftLimit { get; private set; }

    /// <summary>回原速度</summary>
    public decimal HomeSpeed { get; private set; }

    /// <summary>回原加速度</summary>
    public decimal HomeAcceleration { get; private set; }

    /// <summary>原点是否已锁定（完成回原操作后置true）</summary>
    public bool IsOriginLocked { get; private set; }

    // EF Core 所需的无参构造函数
    protected CalibMotorParam() { }

    /// <summary>
    /// 创建电机标定参数
    /// </summary>
    /// <param name="id">实体ID</param>
    /// <param name="calibProjectId">所属标定项目ID</param>
    /// <param name="motorAxisId">电机轴ID</param>
    /// <param name="motorType">电机类型</param>
    public CalibMotorParam(Guid id, Guid calibProjectId, Guid motorAxisId, CalibMotorType motorType)
        : base(id)
    {
        CalibProjectId = calibProjectId;
        MotorAxisId = motorAxisId;
        MotorType = motorType;
        EncoderResolution = 10000;
        GearRatio = 1m;
        IsOriginLocked = false;
    }

    /// <summary>更新编码器与减速比参数</summary>
    public CalibMotorParam SetEncoderParams(int encoderResolution, decimal gearRatio)
    {
        EncoderResolution = encoderResolution;
        GearRatio = gearRatio;
        return this;
    }

    /// <summary>更新原点配置</summary>
    public CalibMotorParam SetOriginConfig(
        decimal mechanicalOriginPosition,
        OriginDirection originDirection
    )
    {
        MechanicalOriginPosition = mechanicalOriginPosition;
        OriginDirection = originDirection;
        return this;
    }

    /// <summary>更新软限位配置</summary>
    public CalibMotorParam SetSoftLimits(decimal positiveSoftLimit, decimal negativeSoftLimit)
    {
        PositiveSoftLimit = positiveSoftLimit;
        NegativeSoftLimit = negativeSoftLimit;
        return this;
    }

    /// <summary>更新回原速度和加速度</summary>
    public CalibMotorParam SetHomingSpeedParams(decimal homeSpeed, decimal homeAcceleration)
    {
        HomeSpeed = homeSpeed;
        HomeAcceleration = homeAcceleration;
        return this;
    }

    /// <summary>锁定/解锁原点</summary>
    public CalibMotorParam SetOriginLocked(bool isLocked)
    {
        IsOriginLocked = isLocked;
        return this;
    }
}
