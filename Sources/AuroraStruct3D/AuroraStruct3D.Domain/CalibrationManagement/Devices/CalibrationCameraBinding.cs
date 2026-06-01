using Volo.Abp.Domain.Entities;

namespace AuroraStruct3D.CalibrationManagement;

/// <summary>
/// 相机绑定：将一台物理相机（<see cref="Cameras.CameraDevice"/>）绑定到标定拓扑中的逻辑角色。
/// 通过 <see cref="CameraDeviceId"/> 跨聚合引用，删除关系时仅清除绑定，不影响相机本体。
/// </summary>
public class CalibrationCameraBinding : Entity<Guid>
{
    /// <summary>所属标定设备 ID（外键）</summary>
    public Guid CalibrationDeviceId { get; private set; }

    /// <summary>被绑定的相机设备 ID（指向 Cameras.CameraDevice.Id）</summary>
    public Guid CameraDeviceId { get; private set; }

    /// <summary>该相机在拓扑中的逻辑角色</summary>
    public CameraRole Role { get; private set; }

    /// <summary>EF Core 构造函数</summary>
    protected CalibrationCameraBinding() { }

    /// <summary>创建相机绑定</summary>
    public CalibrationCameraBinding(
        Guid id,
        Guid calibrationDeviceId,
        Guid cameraDeviceId,
        CameraRole role
    )
        : base(id)
    {
        CalibrationDeviceId = calibrationDeviceId;
        CameraDeviceId = cameraDeviceId;
        Role = role;
    }
}
