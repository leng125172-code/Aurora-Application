using AuroraStruct3D.Calibration;

namespace AuroraStruct3D.Calibration.Dtos;

/// <summary>
/// 设备标定项目输出 DTO
/// </summary>
public class CalibProjectDto
{
    /// <summary>主键</summary>
    public Guid Id { get; set; }

    /// <summary>项目名称</summary>
    public string Name { get; set; } = null!;

    /// <summary>项目描述</summary>
    public string? Description { get; set; }

    /// <summary>设备系列</summary>
    public DeviceSeries DeviceSeries { get; set; }

    /// <summary>设备类型</summary>
    public CalibDeviceType DeviceType { get; set; }

    /// <summary>相机数量</summary>
    public int CameraCount { get; set; }

    /// <summary>结构光数量</summary>
    public int ProjectorCount { get; set; }

    /// <summary>当前标定流程状态</summary>
    public CalibStatus CalibStatus { get; set; }

    /// <summary>创建时间</summary>
    public DateTime CreationTime { get; set; }

    /// <summary>最后修改时间</summary>
    public DateTime? LastModificationTime { get; set; }

    /// <summary>绑定的结构光投影仪设备ID（单光系列 Step 3 选定后持久化，无光系列为 null）</summary>
    public Guid? BoundProjectorDeviceId { get; set; }

    /// <summary>绑定的主相机设备ID</summary>
    public Guid? MainCameraDeviceId { get; set; }

    /// <summary>绑定的从相机设备ID</summary>
    public Guid? SecondaryCameraDeviceId { get; set; }

    /// <summary>绑定的主相机角度控制电机轴ID</summary>
    public Guid? MainCameraMotorAxisId { get; set; }

    /// <summary>绑定的从相机角度控制电机轴ID</summary>
    public Guid? SecondaryCameraMotorAxisId { get; set; }

    /// <summary>绑定的间距控制电机轴ID</summary>
    public Guid? DistanceMotorAxisId { get; set; }
}
