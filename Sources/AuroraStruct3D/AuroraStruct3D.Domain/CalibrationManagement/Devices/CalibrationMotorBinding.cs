using Volo.Abp.Domain.Entities;

namespace AuroraStruct3D.CalibrationManagement;

/// <summary>
/// 电机绑定：将一根物理电机轴（<see cref="Motors.MotorAxis"/>）绑定到标定拓扑中的逻辑角色。
/// 三目设备需通过 <see cref="GimbalGroupId"/> 关联到具体云台组。
/// </summary>
public class CalibrationMotorBinding : Entity<Guid>
{
    /// <summary>所属标定设备 ID</summary>
    public Guid CalibrationDeviceId { get; private set; }

    /// <summary>被绑定的电机轴 ID（指向 Motors.MotorAxis.Id）</summary>
    public Guid MotorAxisId { get; private set; }

    /// <summary>逻辑角色</summary>
    public MotorRole Role { get; private set; }

    /// <summary>所属云台组 ID（可空：仅三目设备的云台轴需填写）</summary>
    public Guid? GimbalGroupId { get; private set; }

    /// <summary>EF Core 构造函数</summary>
    protected CalibrationMotorBinding() { }

    /// <summary>创建电机绑定</summary>
    public CalibrationMotorBinding(
        Guid id,
        Guid calibrationDeviceId,
        Guid motorAxisId,
        MotorRole role,
        Guid? gimbalGroupId = null
    )
        : base(id)
    {
        CalibrationDeviceId = calibrationDeviceId;
        MotorAxisId = motorAxisId;
        Role = role;
        GimbalGroupId = gimbalGroupId;
    }
}
