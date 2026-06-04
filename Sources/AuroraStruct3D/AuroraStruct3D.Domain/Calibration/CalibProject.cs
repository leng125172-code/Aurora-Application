using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// 设备标定项目聚合根。
/// 代表一次完整的多目相机（或结构光）标定工程，
/// 记录设备类型选择和当前标定步骤进度。
///
/// 数据库表：AbpProCalibProjects
/// </summary>
public class CalibProject : FullAuditedAggregateRoot<Guid>
{
    /// <summary>标定项目名称</summary>
    public string Name { get; private set; } = null!;

    /// <summary>标定项目描述</summary>
    public string? Description { get; private set; }

    /// <summary>设备系列（无光系列 / 单光系列）</summary>
    public DeviceSeries DeviceSeries { get; private set; }

    /// <summary>设备类型（2目0光 / 3目0光 / 1目1光 / 2目1光 / 3目1光）</summary>
    public CalibDeviceType DeviceType { get; private set; }

    /// <summary>相机数量（由设备类型决定，1~3）</summary>
    public int CameraCount { get; private set; }

    /// <summary>结构光数量（由设备类型决定，0~1）</summary>
    public int ProjectorCount { get; private set; }

    /// <summary>当前标定流程状态（对应 Step 1~7）</summary>
    public CalibStatus CalibStatus { get; private set; }

    // EF Core 所需的无参构造函数
    protected CalibProject() { }

    /// <summary>
    /// 创建标定项目
    /// </summary>
    /// <param name="id">聚合根ID</param>
    /// <param name="name">项目名称</param>
    /// <param name="deviceType">设备类型</param>
    public CalibProject(Guid id, string name, CalibDeviceType deviceType)
        : base(id)
    {
        SetName(name);
        SetDeviceType(deviceType);
        CalibStatus = CalibStatus.Initializing;
    }

    /// <summary>设置项目名称</summary>
    public CalibProject SetName(string name)
    {
        Check.NotNullOrWhiteSpace(name, nameof(name), CalibConsts.MaxNameLength);
        Name = name;
        return this;
    }

    /// <summary>设置描述</summary>
    public CalibProject SetDescription(string? description)
    {
        if (description != null)
        {
            Check.Length(description, nameof(description), CalibConsts.MaxDescriptionLength);
        }
        Description = description;
        return this;
    }

    /// <summary>设置设备类型，并自动推算相机数量、结构光数量和设备系列</summary>
    public CalibProject SetDeviceType(CalibDeviceType deviceType)
    {
        DeviceType = deviceType;
        (DeviceSeries, CameraCount, ProjectorCount) = deviceType switch
        {
            CalibDeviceType.TwoCamera0Light => (DeviceSeries.NoLight, 2, 0),
            CalibDeviceType.ThreeCamera0Light => (DeviceSeries.NoLight, 3, 0),
            CalibDeviceType.OneCamera1Light => (DeviceSeries.SingleLight, 1, 1),
            CalibDeviceType.TwoCamera1Light => (DeviceSeries.SingleLight, 2, 1),
            CalibDeviceType.ThreeCamera1Light => (DeviceSeries.SingleLight, 3, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(deviceType)),
        };
        return this;
    }

    /// <summary>推进标定步骤状态</summary>
    public CalibProject AdvanceStatus(CalibStatus status)
    {
        CalibStatus = status;
        return this;
    }
}
