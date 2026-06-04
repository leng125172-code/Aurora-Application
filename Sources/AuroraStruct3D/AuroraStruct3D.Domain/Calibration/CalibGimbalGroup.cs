using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// 多轴云台组实体（Step 4，仅三目设备使用）。
/// 云台组是由多个电机轴组合而成的独立逻辑设备，不绑定具体标定项目，
/// 可在不同项目中通过 CalibDeviceBinding.BoundGimbalGroupId 引用。
/// 每套设备最多配置 3 个独立云台组。
///
/// 数据库表：AbpProCalibGimbalGroups
/// </summary>
public class CalibGimbalGroup : FullAuditedEntity<Guid>
{
    /// <summary>云台组名称</summary>
    public string Name { get; private set; } = null!;

    /// <summary>云台组描述</summary>
    public string? Description { get; private set; }

    /// <summary>云台组最大运动速度</summary>
    public decimal MaxSpeed { get; private set; }

    /// <summary>云台组加速度</summary>
    public decimal Acceleration { get; private set; }

    /// <summary>加速时间（ms）</summary>
    public decimal AccelerationTime { get; private set; }

    /// <summary>减速时间（ms）</summary>
    public decimal DecelerationTime { get; private set; }

    /// <summary>是否启用此云台组</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>该云台组下的电机轴绑定列表</summary>
    public ICollection<CalibGimbalBinding> Bindings { get; private set; } =
        new List<CalibGimbalBinding>();

    // EF Core 所需的无参构造函数
    protected CalibGimbalGroup() { }

    /// <summary>
    /// 创建云台组
    /// </summary>
    /// <param name="id">实体ID</param>
    /// <param name="name">云台组名称</param>
    public CalibGimbalGroup(Guid id, string name)
        : base(id)
    {
        SetName(name);
        IsEnabled = true;
    }

    /// <summary>设置云台组名称</summary>
    public CalibGimbalGroup SetName(string name)
    {
        Check.NotNullOrWhiteSpace(name, nameof(name), CalibConsts.MaxNameLength);
        Name = name;
        return this;
    }

    /// <summary>设置描述</summary>
    public CalibGimbalGroup SetDescription(string? description)
    {
        if (description != null)
        {
            Check.Length(description, nameof(description), CalibConsts.MaxDescriptionLength);
        }
        Description = description;
        return this;
    }

    /// <summary>更新云台组运动参数</summary>
    public CalibGimbalGroup SetMotionParams(
        decimal maxSpeed,
        decimal acceleration,
        decimal accelerationTime,
        decimal decelerationTime
    )
    {
        MaxSpeed = maxSpeed;
        Acceleration = acceleration;
        AccelerationTime = accelerationTime;
        DecelerationTime = decelerationTime;
        return this;
    }

    /// <summary>启用或禁用此云台组</summary>
    public CalibGimbalGroup SetEnabled(bool isEnabled)
    {
        IsEnabled = isEnabled;
        return this;
    }
}
