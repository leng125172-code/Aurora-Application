using Volo.Abp.Domain.Entities;

namespace AuroraStruct3D.CalibrationManagement;

/// <summary>
/// 联动软限位规则：当 <see cref="SourceMotorAxisId"/> 处于
/// [<see cref="SourcePositionMin"/>, <see cref="SourcePositionMax"/>] 范围内时，
/// 禁止 <see cref="TargetMotorAxisId"/> 沿 <see cref="BlockedDirection"/> 方向运动。
/// 由后台位置监控服务实时校验。
/// </summary>
public class CalibrationMotorInterlockRule : Entity<Guid>
{
    /// <summary>所属标定设备 ID</summary>
    public Guid CalibrationDeviceId { get; private set; }

    /// <summary>触发源电机轴 ID</summary>
    public Guid SourceMotorAxisId { get; private set; }

    /// <summary>触发位置下限</summary>
    public double SourcePositionMin { get; private set; }

    /// <summary>触发位置上限</summary>
    public double SourcePositionMax { get; private set; }

    /// <summary>被限制的目标电机轴 ID</summary>
    public Guid TargetMotorAxisId { get; private set; }

    /// <summary>被禁止的运动方向</summary>
    public BlockedMotionDirection BlockedDirection { get; private set; }

    /// <summary>规则是否启用</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>规则备注</summary>
    public string? Description { get; private set; }

    /// <summary>EF Core 构造函数</summary>
    protected CalibrationMotorInterlockRule() { }

    /// <summary>创建联动软限位规则</summary>
    public CalibrationMotorInterlockRule(
        Guid id,
        Guid calibrationDeviceId,
        Guid sourceMotorAxisId,
        double sourcePositionMin,
        double sourcePositionMax,
        Guid targetMotorAxisId,
        BlockedMotionDirection blockedDirection,
        string? description = null
    )
        : base(id)
    {
        CalibrationDeviceId = calibrationDeviceId;
        SourceMotorAxisId = sourceMotorAxisId;
        SourcePositionMin = sourcePositionMin;
        SourcePositionMax = sourcePositionMax;
        TargetMotorAxisId = targetMotorAxisId;
        BlockedDirection = blockedDirection;
        Description = description;
        IsEnabled = true;
    }

    /// <summary>设置启用状态</summary>
    public void SetEnabled(bool isEnabled) => IsEnabled = isEnabled;
}
