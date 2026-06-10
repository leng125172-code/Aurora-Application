using AuroraStruct3D.Cameras;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Cameras;

/// <summary>
/// 鑫图相机设备聚合根。
/// 对应一台物理相机，存储其基本信息和连接配置。
/// </summary>
public class CameraDevice : FullAuditedAggregateRoot<Guid>
{
    /// <summary>相机显示名称</summary>
    public string Name { get; private set; } = null!;

    /// <summary>相机型号（从SDK读取）</summary>
    public string? Model { get; private set; }

    /// <summary>设备序列号（来自 DeviceControl/DeviceSerialNumber）</summary>
    public string? DeviceSerialNumber { get; private set; }

    /// <summary>物理索引（SDK中的相机位置，从0开始）</summary>
    public int DeviceIndex { get; private set; }

    /// <summary>当前状态</summary>
    public CameraStatus Status { get; private set; }

    /// <summary>备注描述</summary>
    public string? Description { get; private set; }

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>当前激活的参数集ID（可为空，表示使用默认值）</summary>
    public Guid? ActiveParameterSetId { get; private set; }

    /// <summary>图像顺时针旋转角度（度，仅支持 0/90/180/270）</summary>
    public int ImageRotationAngle { get; private set; }

    /// <summary>该相机下的所有参数集</summary>
    public ICollection<CameraParameterSet> ParameterSets { get; private set; } =
        new List<CameraParameterSet>();

    // EF Core 所需的无参构造函数
    protected CameraDevice() { }

    /// <summary>
    /// 创建相机设备
    /// </summary>
    /// <param name="id">聚合根ID</param>
    /// <param name="name">相机名称</param>
    /// <param name="deviceIndex">设备物理索引</param>
    public CameraDevice(Guid id, string name, int deviceIndex)
        : base(id)
    {
        SetName(name);
        DeviceIndex = deviceIndex;
        Status = CameraStatus.Unknown;
        IsEnabled = true;
        ImageRotationAngle = 0;
    }

    /// <summary>设置相机名称</summary>
    public CameraDevice SetName(string name)
    {
        Check.NotNullOrWhiteSpace(name, nameof(name), CameraConsts.MaxNameLength);
        Name = name;
        return this;
    }

    /// <summary>更新相机硬件信息（由SDK读取后更新）</summary>
    public CameraDevice UpdateHardwareInfo(string? model)
    {
        if (model != null)
        {
            Check.Length(model, nameof(model), CameraConsts.MaxNameLength);
        }
        Model = model;
        return this;
    }

    /// <summary>更新设备序列号（空字符串会被归一化为 null）</summary>
    public CameraDevice UpdateDeviceSerialNumber(string? serialNumber)
    {
        string? normalized = string.IsNullOrWhiteSpace(serialNumber) ? null : serialNumber.Trim();
        if (normalized != null)
        {
            Check.Length(normalized, nameof(serialNumber), CameraConsts.MaxSerialNumberLength);
        }

        DeviceSerialNumber = normalized;
        return this;
    }

    /// <summary>更新相机索引（用于序列号重映射）</summary>
    public CameraDevice SetDeviceIndex(int deviceIndex)
    {
        Check.Range(deviceIndex, nameof(deviceIndex), 0, int.MaxValue);
        DeviceIndex = deviceIndex;
        return this;
    }

    /// <summary>更新状态</summary>
    public CameraDevice SetStatus(CameraStatus status)
    {
        Status = status;
        return this;
    }

    /// <summary>设置描述</summary>
    public CameraDevice SetDescription(string? description)
    {
        if (description != null)
        {
            Check.Length(description, nameof(description), CameraConsts.MaxDescriptionLength);
        }
        Description = description;
        return this;
    }

    /// <summary>启用或禁用相机</summary>
    public CameraDevice SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
        return this;
    }

    /// <summary>激活指定参数集</summary>
    public CameraDevice ActivateParameterSet(Guid? parameterSetId)
    {
        ActiveParameterSetId = parameterSetId;
        return this;
    }

    /// <summary>
    /// 设置图像顺时针旋转角度。
    /// </summary>
    /// <param name="angle">旋转角度，仅支持 0、90、180、270 度。</param>
    /// <returns>当前相机设备实体。</returns>
    public CameraDevice SetImageRotationAngle(int angle)
    {
        ImageRotationAngle = NormalizeImageRotationAngle(angle);
        return this;
    }

    private static int NormalizeImageRotationAngle(int angle)
    {
        int normalizedAngle = angle % 360;
        if (normalizedAngle < 0)
        {
            normalizedAngle += 360;
        }

        return normalizedAngle switch
        {
            0 or 90 or 180 or 270 => normalizedAngle,
            _ => throw new ArgumentOutOfRangeException(
                nameof(angle),
                "图像旋转角度仅支持 0、90、180、270 度"
            ),
        };
    }
}
