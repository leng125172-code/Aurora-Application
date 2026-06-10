using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// 设备绑定关系实体（Step 7）。
/// 记录标定项目中物理设备与逻辑角色的绑定关系，
/// 涵盖相机绑定、电机绑定和结构光投射器绑定三种类型。
///
/// 数据库表：AbpProCalibDeviceBindings
/// </summary>
public class CalibDeviceBinding : FullAuditedEntity<Guid>
{
    /// <summary>所属标定项目ID（关联AbpProCalibProjects）</summary>
    public Guid CalibProjectId { get; private set; }

    /// <summary>绑定类型（相机 / 电机 / 投射器）</summary>
    public CalibBindingType BindingType { get; private set; }

    /// <summary>
    /// 源设备ID：
    /// 当BindingType=Camera时关联AbpProCameraDevices；
    /// 当BindingType=Motor时关联AbpProMotorAxes；
    /// 当BindingType=Projector时关联AbpProProjectors
    /// </summary>
    public Guid SourceDeviceId { get; private set; }

    /// <summary>目标逻辑角色名称（如"主相机"、"左相机旋转电机"、"主结构光"）</summary>
    public string TargetRole { get; private set; } = null!;

    /// <summary>关联的相机参数配置ID（仅BindingType=Camera时有效）</summary>
    public Guid? BoundCameraParamId { get; private set; }

    /// <summary>关联的电机参数配置ID（仅BindingType=Motor时有效）</summary>
    public Guid? BoundMotorParamId { get; private set; }

    /// <summary>关联的投射器参数配置ID（仅BindingType=Projector时有效）</summary>
    public Guid? BoundProjectorParamId { get; private set; }

    /// <summary>绑定状态（待绑定 / 已绑定 / 验证中 / 验证失败）</summary>
    public CalibBindingStatus BindingStatus { get; private set; }

    /// <summary>状态信息（失败时记录原因）</summary>
    public string? StatusMessage { get; private set; }

    // EF Core 所需的无参构造函数
    protected CalibDeviceBinding() { }

    /// <summary>
    /// 创建设备绑定记录
    /// </summary>
    /// <param name="id">实体ID</param>
    /// <param name="calibProjectId">所属标定项目ID</param>
    /// <param name="bindingType">绑定类型</param>
    /// <param name="sourceDeviceId">源设备ID</param>
    /// <param name="targetRole">目标逻辑角色</param>
    public CalibDeviceBinding(
        Guid id,
        Guid calibProjectId,
        CalibBindingType bindingType,
        Guid sourceDeviceId,
        string targetRole
    )
        : base(id)
    {
        CalibProjectId = calibProjectId;
        BindingType = bindingType;
        SourceDeviceId = sourceDeviceId;
        SetTargetRole(targetRole);
        BindingStatus = CalibBindingStatus.Pending;
    }

    /// <summary>设置目标逻辑角色名称</summary>
    public CalibDeviceBinding SetTargetRole(string targetRole)
    {
        Check.NotNullOrWhiteSpace(targetRole, nameof(targetRole), CalibConsts.MaxTargetRoleLength);
        TargetRole = targetRole;
        return this;
    }

    /// <summary>完成相机绑定</summary>
    public CalibDeviceBinding BindCamera(Guid cameraParamId)
    {
        BoundCameraParamId = cameraParamId;
        BindingStatus = CalibBindingStatus.Bound;
        StatusMessage = null;
        return this;
    }

    /// <summary>完成电机绑定（非云台设备）</summary>
    public CalibDeviceBinding BindMotor(Guid motorParamId)
    {
        BoundMotorParamId = motorParamId;
        BindingStatus = CalibBindingStatus.Bound;
        StatusMessage = null;
        return this;
    }

    /// <summary>完成投射器绑定</summary>
    public CalibDeviceBinding BindProjector(Guid projectorParamId)
    {
        BoundProjectorParamId = projectorParamId;
        BindingStatus = CalibBindingStatus.Bound;
        StatusMessage = null;
        return this;
    }

    /// <summary>设置绑定状态（用于验证流程）</summary>
    public CalibDeviceBinding SetStatus(CalibBindingStatus status, string? message = null)
    {
        BindingStatus = status;
        if (message != null)
        {
            Check.Length(message, nameof(message), CalibConsts.MaxStatusMessageLength);
        }
        StatusMessage = message;
        return this;
    }
}
