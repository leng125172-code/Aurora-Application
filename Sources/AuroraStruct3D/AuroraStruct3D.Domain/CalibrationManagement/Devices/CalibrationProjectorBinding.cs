using Volo.Abp.Domain.Entities;

namespace AuroraStruct3D.CalibrationManagement;

/// <summary>
/// 结构光投射器绑定（仅单光系列设备使用）。
/// 通过 <see cref="ProjectorDeviceId"/> 关联到 Projectors.ProjectorDevice。
/// </summary>
public class CalibrationProjectorBinding : Entity<Guid>
{
    /// <summary>所属标定设备 ID</summary>
    public Guid CalibrationDeviceId { get; private set; }

    /// <summary>被绑定的投射器设备 ID（指向 Projectors.ProjectorDevice.Id）</summary>
    public Guid ProjectorDeviceId { get; private set; }

    /// <summary>逻辑角色</summary>
    public ProjectorRole Role { get; private set; }

    /// <summary>EF Core 构造函数</summary>
    protected CalibrationProjectorBinding() { }

    /// <summary>创建结构光绑定</summary>
    public CalibrationProjectorBinding(
        Guid id,
        Guid calibrationDeviceId,
        Guid projectorDeviceId,
        ProjectorRole role
    )
        : base(id)
    {
        CalibrationDeviceId = calibrationDeviceId;
        ProjectorDeviceId = projectorDeviceId;
        Role = role;
    }
}
