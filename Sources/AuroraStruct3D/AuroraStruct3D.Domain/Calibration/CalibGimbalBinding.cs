using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// 云台电机轴绑定实体（Step 4 从表）。
/// 记录云台组与电机轴的绑定关系及轴类型（X旋转、Y旋转、Z旋转、Z平移）。
///
/// 数据库表：AbpProCalibGimbalBindings
/// </summary>
public class CalibGimbalBinding : FullAuditedEntity<Guid>
{
    /// <summary>所属云台组ID（关联AbpProCalibGimbalGroups）</summary>
    public Guid GimbalGroupId { get; private set; }

    /// <summary>绑定的电机轴ID（关联AbpProMotorAxes）</summary>
    public Guid MotorAxisId { get; private set; }

    /// <summary>该电机轴在云台中的轴类型</summary>
    public GimbalAxisType AxisType { get; private set; }

    /// <summary>是否必需轴（X轴旋转和Y轴旋转为必需，Z轴可选）</summary>
    public bool IsRequired { get; private set; }

    // EF Core 所需的无参构造函数
    protected CalibGimbalBinding() { }

    /// <summary>
    /// 创建云台电机轴绑定
    /// </summary>
    /// <param name="id">实体ID</param>
    /// <param name="gimbalGroupId">所属云台组ID</param>
    /// <param name="motorAxisId">电机轴ID</param>
    /// <param name="axisType">轴类型</param>
    public CalibGimbalBinding(
        Guid id,
        Guid gimbalGroupId,
        Guid motorAxisId,
        GimbalAxisType axisType
    )
        : base(id)
    {
        GimbalGroupId = gimbalGroupId;
        MotorAxisId = motorAxisId;
        AxisType = axisType;
        // X轴旋转和Y轴旋转为必需，Z轴相关为可选
        IsRequired = axisType == GimbalAxisType.RotateX || axisType == GimbalAxisType.RotateY;
    }

    /// <summary>更新绑定的电机轴</summary>
    public CalibGimbalBinding SetMotorAxis(Guid motorAxisId)
    {
        MotorAxisId = motorAxisId;
        return this;
    }
}
