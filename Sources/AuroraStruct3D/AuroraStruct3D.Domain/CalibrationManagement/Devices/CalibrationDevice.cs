using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.CalibrationManagement;

/// <summary>
/// 标定设备聚合根（Step 1~3 的配置入口）。
/// 持有：设备拓扑类型、相机/电机/投射器绑定、云台组、电机互锁规则、各硬件参数快照。
/// 通过 Guid 跨聚合引用现有 <see cref="Cameras.CameraDevice"/>、<see cref="Motors.MotorAxis"/>、
/// <see cref="Projectors.ProjectorDevice"/>，本聚合不修改外部聚合。
/// </summary>
public class CalibrationDevice : FullAuditedAggregateRoot<Guid>
{
    /// <summary>设备显示名称</summary>
    public string Name { get; private set; } = null!;

    /// <summary>描述</summary>
    public string? Description { get; private set; }

    /// <summary>设备拓扑类型</summary>
    public CalibrationDeviceType DeviceType { get; private set; }

    /// <summary>是否启用</summary>
    public bool IsActive { get; private set; }

    /// <summary>相机绑定集合</summary>
    public ICollection<CalibrationCameraBinding> CameraBindings { get; private set; } =
        new List<CalibrationCameraBinding>();

    /// <summary>电机绑定集合</summary>
    public ICollection<CalibrationMotorBinding> MotorBindings { get; private set; } =
        new List<CalibrationMotorBinding>();

    /// <summary>结构光投射器绑定集合（仅单光系列）</summary>
    public ICollection<CalibrationProjectorBinding> ProjectorBindings { get; private set; } =
        new List<CalibrationProjectorBinding>();

    /// <summary>云台组集合（仅 3 目设备，最多 3 个）</summary>
    public ICollection<CalibrationGimbalGroup> GimbalGroups { get; private set; } =
        new List<CalibrationGimbalGroup>();

    /// <summary>电机联动软限位规则集合</summary>
    public ICollection<CalibrationMotorInterlockRule> InterlockRules { get; private set; } =
        new List<CalibrationMotorInterlockRule>();

    /// <summary>相机硬件参数快照集合</summary>
    public ICollection<CalibrationCameraParameter> CameraParameters { get; private set; } =
        new List<CalibrationCameraParameter>();

    /// <summary>投射器硬件参数快照集合（仅单光系列）</summary>
    public ICollection<CalibrationProjectorParameter> ProjectorParameters { get; private set; } =
        new List<CalibrationProjectorParameter>();

    /// <summary>电机参数快照集合</summary>
    public ICollection<CalibrationMotorParameter> MotorParameters { get; private set; } =
        new List<CalibrationMotorParameter>();

    /// <summary>EF Core 构造函数</summary>
    protected CalibrationDevice() { }

    /// <summary>创建标定设备配置</summary>
    public CalibrationDevice(
        Guid id,
        string name,
        CalibrationDeviceType deviceType,
        string? description = null
    )
        : base(id)
    {
        Name = Check.NotNullOrWhiteSpace(
            name,
            nameof(name),
            CalibrationManagementConsts.MaxNameLength
        );
        DeviceType = deviceType;
        Description = description;
        IsActive = true;
    }

    /// <summary>更新名称与描述</summary>
    public void UpdateBasicInfo(string name, string? description)
    {
        Name = Check.NotNullOrWhiteSpace(
            name,
            nameof(name),
            CalibrationManagementConsts.MaxNameLength
        );
        Description = description;
    }

    /// <summary>
    /// 切换设备拓扑类型。
    /// 调用方需先清理与新类型不兼容的绑定/参数；本方法不做隐式清理。
    /// </summary>
    public void ChangeDeviceType(CalibrationDeviceType deviceType) => DeviceType = deviceType;

    /// <summary>设置启用状态</summary>
    public void SetActive(bool isActive) => IsActive = isActive;

    /// <summary>
    /// 校验当前绑定是否满足设备拓扑要求。
    /// 仅占位：完整规则在 Step 1/2 的应用服务里实现，避免本批次过度抽象。
    /// </summary>
    public bool ValidateBindingsForDeviceType()
    {
        return CameraBindings.Count == RequiredCameraCount(DeviceType);
    }

    /// <summary>根据设备类型返回必须绑定的相机数量</summary>
    public static int RequiredCameraCount(CalibrationDeviceType deviceType) =>
        deviceType switch
        {
            CalibrationDeviceType.OneCamOneLight => 1,
            CalibrationDeviceType.TwoCamZeroLight => 2,
            CalibrationDeviceType.TwoCamOneLight => 2,
            CalibrationDeviceType.ThreeCamZeroLight => 3,
            CalibrationDeviceType.ThreeCamOneLight => 3,
            _ => 0,
        };

    /// <summary>设备类型是否包含结构光</summary>
    public static bool HasStructuredLight(CalibrationDeviceType deviceType) =>
        deviceType
            is CalibrationDeviceType.OneCamOneLight
                or CalibrationDeviceType.TwoCamOneLight
                or CalibrationDeviceType.ThreeCamOneLight;
}
