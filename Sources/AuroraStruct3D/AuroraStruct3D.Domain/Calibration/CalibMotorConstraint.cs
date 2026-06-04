using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// 电机联动限制规则实体（Step 3）。
/// 定义联动软限位规则：当电机A处于指定位置范围时，
/// 禁止电机B执行指定方向的运动操作。
///
/// 数据库表：AbpProCalibMotorConstraints
/// </summary>
public class CalibMotorConstraint : FullAuditedEntity<Guid>
{
    /// <summary>所属标定项目ID（关联AbpProCalibProjects）</summary>
    public Guid CalibProjectId { get; private set; }

    /// <summary>触发条件电机A的ID（关联AbpProMotorAxes）</summary>
    public Guid MotorAId { get; private set; }

    /// <summary>电机A触发位置范围下限</summary>
    public decimal PositionRangeMin { get; private set; }

    /// <summary>电机A触发位置范围上限</summary>
    public decimal PositionRangeMax { get; private set; }

    /// <summary>被限制电机B的ID（关联AbpProMotorAxes）</summary>
    public Guid MotorBId { get; private set; }

    /// <summary>禁止电机B执行的运动方向</summary>
    public ForbiddenDirection ForbiddenDirection { get; private set; }

    /// <summary>规则描述（便于管理员理解规则用途）</summary>
    public string? RuleDescription { get; private set; }

    /// <summary>是否启用此联动限制规则</summary>
    public bool IsEnabled { get; private set; }

    // EF Core 所需的无参构造函数
    protected CalibMotorConstraint() { }

    /// <summary>
    /// 创建电机联动限制规则
    /// </summary>
    /// <param name="id">实体ID</param>
    /// <param name="calibProjectId">所属标定项目ID</param>
    /// <param name="motorAId">触发条件电机A ID</param>
    /// <param name="motorBId">被限制电机B ID</param>
    /// <param name="positionRangeMin">电机A触发范围下限</param>
    /// <param name="positionRangeMax">电机A触发范围上限</param>
    /// <param name="forbiddenDirection">禁止方向</param>
    public CalibMotorConstraint(
        Guid id,
        Guid calibProjectId,
        Guid motorAId,
        Guid motorBId,
        decimal positionRangeMin,
        decimal positionRangeMax,
        ForbiddenDirection forbiddenDirection
    )
        : base(id)
    {
        CalibProjectId = calibProjectId;
        MotorAId = motorAId;
        MotorBId = motorBId;
        PositionRangeMin = positionRangeMin;
        PositionRangeMax = positionRangeMax;
        ForbiddenDirection = forbiddenDirection;
        IsEnabled = true;
    }

    /// <summary>更新联动限制规则参数</summary>
    public CalibMotorConstraint Update(
        Guid motorAId,
        Guid motorBId,
        decimal positionRangeMin,
        decimal positionRangeMax,
        ForbiddenDirection forbiddenDirection,
        string? ruleDescription
    )
    {
        MotorAId = motorAId;
        MotorBId = motorBId;
        PositionRangeMin = positionRangeMin;
        PositionRangeMax = positionRangeMax;
        ForbiddenDirection = forbiddenDirection;

        if (ruleDescription != null)
        {
            Check.Length(
                ruleDescription,
                nameof(ruleDescription),
                CalibConsts.MaxRuleDescriptionLength
            );
        }
        RuleDescription = ruleDescription;
        return this;
    }

    /// <summary>启用或禁用此规则</summary>
    public CalibMotorConstraint SetEnabled(bool isEnabled)
    {
        IsEnabled = isEnabled;
        return this;
    }
}
