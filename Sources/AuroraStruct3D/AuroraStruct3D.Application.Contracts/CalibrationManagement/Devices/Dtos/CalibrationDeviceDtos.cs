using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace AuroraStruct3D.CalibrationManagement.Devices.Dtos;

// ═══════════════════════════════════════════════════════════════════════════
// 输出 DTO
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// 标定设备列表项 DTO（不含子集合，列表查询使用）。
/// </summary>
public class CalibrationDeviceListDto : FullAuditedEntityDto<Guid>
{
    /// <summary>设备显示名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>描述</summary>
    public string? Description { get; set; }

    /// <summary>设备拓扑类型</summary>
    public CalibrationDeviceType DeviceType { get; set; }

    /// <summary>是否启用</summary>
    public bool IsActive { get; set; }

    /// <summary>已绑定的相机数量</summary>
    public int CameraBindingCount { get; set; }

    /// <summary>已绑定的电机数量</summary>
    public int MotorBindingCount { get; set; }

    /// <summary>已绑定的投射器数量</summary>
    public int ProjectorBindingCount { get; set; }

    /// <summary>该拓扑类型是否包含结构光</summary>
    public bool HasStructuredLight { get; set; }
}

/// <summary>
/// 标定设备详情 DTO（含所有子集合）。
/// </summary>
public class CalibrationDeviceDetailDto : CalibrationDeviceListDto
{
    /// <summary>相机绑定</summary>
    public List<CalibrationCameraBindingDto> CameraBindings { get; set; } = new();

    /// <summary>电机绑定</summary>
    public List<CalibrationMotorBindingDto> MotorBindings { get; set; } = new();

    /// <summary>投射器绑定</summary>
    public List<CalibrationProjectorBindingDto> ProjectorBindings { get; set; } = new();

    /// <summary>云台组（含预设位置）</summary>
    public List<CalibrationGimbalGroupDto> GimbalGroups { get; set; } = new();

    /// <summary>电机互锁规则</summary>
    public List<CalibrationMotorInterlockRuleDto> InterlockRules { get; set; } = new();

    /// <summary>相机参数</summary>
    public List<CalibrationCameraParameterDto> CameraParameters { get; set; } = new();

    /// <summary>投射器参数</summary>
    public List<CalibrationProjectorParameterDto> ProjectorParameters { get; set; } = new();

    /// <summary>电机参数</summary>
    public List<CalibrationMotorParameterDto> MotorParameters { get; set; } = new();
}

// ═══════════════════════════════════════════════════════════════════════════
// 子实体 DTO（绑定 / 云台 / 互锁规则）
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>相机绑定 DTO</summary>
public class CalibrationCameraBindingDto : EntityDto<Guid>
{
    public Guid CalibrationDeviceId { get; set; }
    public Guid CameraDeviceId { get; set; }
    public CameraRole Role { get; set; }
}

/// <summary>电机绑定 DTO</summary>
public class CalibrationMotorBindingDto : EntityDto<Guid>
{
    public Guid CalibrationDeviceId { get; set; }
    public Guid MotorAxisId { get; set; }
    public MotorRole Role { get; set; }
    public Guid? GimbalGroupId { get; set; }
}

/// <summary>投射器绑定 DTO</summary>
public class CalibrationProjectorBindingDto : EntityDto<Guid>
{
    public Guid CalibrationDeviceId { get; set; }
    public Guid ProjectorDeviceId { get; set; }
    public ProjectorRole Role { get; set; }
}

/// <summary>云台组 DTO</summary>
public class CalibrationGimbalGroupDto : EntityDto<Guid>
{
    public Guid CalibrationDeviceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid XAxisMotorId { get; set; }
    public Guid YAxisMotorId { get; set; }
    public Guid? ZRotateAxisMotorId { get; set; }
    public Guid? ZTranslateAxisMotorId { get; set; }
    public double MaxVelocity { get; set; }
    public double Acceleration { get; set; }
    public double AccelDecelTime { get; set; }
    public List<CalibrationGimbalPresetDto> PresetPositions { get; set; } = new();
}

/// <summary>云台预设位置 DTO</summary>
public class CalibrationGimbalPresetDto : EntityDto<Guid>
{
    public Guid GimbalGroupId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Remarks { get; set; }
    public double XPosition { get; set; }
    public double YPosition { get; set; }
    public double? ZRotatePosition { get; set; }
    public double? ZTranslatePosition { get; set; }
}

/// <summary>电机联动软限位规则 DTO</summary>
public class CalibrationMotorInterlockRuleDto : EntityDto<Guid>
{
    public Guid CalibrationDeviceId { get; set; }
    public Guid SourceMotorAxisId { get; set; }
    public double SourcePositionMin { get; set; }
    public double SourcePositionMax { get; set; }
    public Guid TargetMotorAxisId { get; set; }
    public BlockedMotionDirection BlockedDirection { get; set; }
    public bool IsEnabled { get; set; }
    public string? Description { get; set; }
}

// ═══════════════════════════════════════════════════════════════════════════
// 硬件参数 DTO（相机 / 投射器 / 电机）
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>相机硬件参数 DTO</summary>
public class CalibrationCameraParameterDto : EntityDto<Guid>
{
    public Guid CalibrationDeviceId { get; set; }
    public Guid CameraDeviceId { get; set; }
    public CameraRole Role { get; set; }

    public CmosSensorSize CmosSize { get; set; }
    public double CmosWidthMm { get; set; }
    public double CmosHeightMm { get; set; }

    public int ResolutionWidthPx { get; set; }
    public int ResolutionHeightPx { get; set; }

    public double NominalFocalLengthMm { get; set; }
    public double MaxAperture { get; set; }
    public double MinAperture { get; set; }
    public double CurrentAperture { get; set; }

    public double MinExposureUs { get; set; }
    public double MaxExposureUs { get; set; }
    public double MinGainDb { get; set; }
    public double MaxGainDb { get; set; }

    /// <summary>派生字段：像素物理尺寸（μm/像素）</summary>
    public double PixelSizeUm { get; set; }
}

/// <summary>结构光投射器硬件参数 DTO</summary>
public class CalibrationProjectorParameterDto : EntityDto<Guid>
{
    public Guid CalibrationDeviceId { get; set; }
    public Guid ProjectorDeviceId { get; set; }
    public int ResolutionWidthPx { get; set; }
    public int ResolutionHeightPx { get; set; }
    public double ThrowRatio { get; set; }
    public double MinWorkingDistanceMm { get; set; }
    public double MaxWorkingDistanceMm { get; set; }
    public StructuredLightPattern Pattern { get; set; }
    public int PatternCount { get; set; }
    public double PhaseShift { get; set; }
}

/// <summary>电机硬件参数 DTO</summary>
public class CalibrationMotorParameterDto : EntityDto<Guid>
{
    public Guid CalibrationDeviceId { get; set; }
    public Guid MotorAxisId { get; set; }
    public MotorRole Role { get; set; }

    public int EncoderResolution { get; set; }
    public double GearRatio { get; set; }

    public double HomePosition { get; set; }
    public int HomeDirection { get; set; }
    public double HomingVelocity { get; set; }
    public double HomingAcceleration { get; set; }

    public double SoftLimitMin { get; set; }
    public double SoftLimitMax { get; set; }

    public bool IsHomed { get; set; }
    public DateTime? LastHomedTime { get; set; }
}

// ═══════════════════════════════════════════════════════════════════════════
// 输入 DTO（查询 / 增删改）
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>查询标定设备列表的输入 DTO</summary>
public class GetCalibrationDeviceListInput : PagedAndSortedResultRequestDto
{
    /// <summary>名称模糊搜索</summary>
    public string? Filter { get; set; }

    /// <summary>按设备拓扑类型过滤</summary>
    public CalibrationDeviceType? DeviceType { get; set; }

    /// <summary>仅显示启用的</summary>
    public bool? IsActive { get; set; }
}

/// <summary>创建/更新标定设备的基础信息</summary>
public class CreateUpdateCalibrationDeviceDto
{
    /// <summary>设备显示名称（必填）</summary>
    [Required]
    [StringLength(CalibrationManagementConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    /// <summary>描述（可选）</summary>
    [StringLength(CalibrationManagementConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>设备拓扑类型（必填）</summary>
    [Required]
    public CalibrationDeviceType DeviceType { get; set; }
}

/// <summary>更新启用状态的输入</summary>
public class SetCalibrationDeviceActiveInput
{
    public bool IsActive { get; set; }
}

// ── 绑定输入 ─────────────────────────────────────────────────────────────

/// <summary>新增/替换相机绑定</summary>
public class UpsertCameraBindingInput
{
    [Required]
    public Guid CameraDeviceId { get; set; }

    [Required]
    public CameraRole Role { get; set; }
}

/// <summary>新增/替换电机绑定</summary>
public class UpsertMotorBindingInput
{
    [Required]
    public Guid MotorAxisId { get; set; }

    [Required]
    public MotorRole Role { get; set; }

    /// <summary>所属云台组 ID（仅云台轴需要）</summary>
    public Guid? GimbalGroupId { get; set; }
}

/// <summary>新增/替换投射器绑定</summary>
public class UpsertProjectorBindingInput
{
    [Required]
    public Guid ProjectorDeviceId { get; set; }

    [Required]
    public ProjectorRole Role { get; set; }
}

// ── 云台组输入 ───────────────────────────────────────────────────────────

/// <summary>创建/更新云台组</summary>
public class CreateUpdateGimbalGroupInput
{
    [Required]
    [StringLength(CalibrationManagementConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public Guid XAxisMotorId { get; set; }

    [Required]
    public Guid YAxisMotorId { get; set; }

    public Guid? ZRotateAxisMotorId { get; set; }

    public Guid? ZTranslateAxisMotorId { get; set; }

    public double MaxVelocity { get; set; }
    public double Acceleration { get; set; }
    public double AccelDecelTime { get; set; }
}

/// <summary>创建云台预设位置</summary>
public class CreateGimbalPresetInput
{
    [Required]
    [StringLength(CalibrationManagementConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    public string? Remarks { get; set; }

    [Required]
    public double XPosition { get; set; }

    [Required]
    public double YPosition { get; set; }

    public double? ZRotatePosition { get; set; }
    public double? ZTranslatePosition { get; set; }
}

// ── 互锁规则输入 ─────────────────────────────────────────────────────────

/// <summary>创建/更新电机联动软限位规则</summary>
public class CreateUpdateInterlockRuleInput
{
    [Required]
    public Guid SourceMotorAxisId { get; set; }

    [Required]
    public double SourcePositionMin { get; set; }

    [Required]
    public double SourcePositionMax { get; set; }

    [Required]
    public Guid TargetMotorAxisId { get; set; }

    [Required]
    public BlockedMotionDirection BlockedDirection { get; set; }

    public bool IsEnabled { get; set; } = true;

    [StringLength(CalibrationManagementConsts.MaxDescriptionLength)]
    public string? Description { get; set; }
}

// ── 硬件参数输入 ─────────────────────────────────────────────────────────

/// <summary>创建/更新相机硬件参数</summary>
public class CreateUpdateCameraParameterInput
{
    [Required]
    public Guid CameraDeviceId { get; set; }

    [Required]
    public CameraRole Role { get; set; }

    public CmosSensorSize CmosSize { get; set; }
    public double CmosWidthMm { get; set; }
    public double CmosHeightMm { get; set; }

    public int ResolutionWidthPx { get; set; }
    public int ResolutionHeightPx { get; set; }

    public double NominalFocalLengthMm { get; set; }
    public double MaxAperture { get; set; }
    public double MinAperture { get; set; }
    public double CurrentAperture { get; set; }

    public double MinExposureUs { get; set; }
    public double MaxExposureUs { get; set; }
    public double MinGainDb { get; set; }
    public double MaxGainDb { get; set; }
}

/// <summary>创建/更新结构光投射器硬件参数</summary>
public class CreateUpdateProjectorParameterInput
{
    [Required]
    public Guid ProjectorDeviceId { get; set; }

    public int ResolutionWidthPx { get; set; }
    public int ResolutionHeightPx { get; set; }
    public double ThrowRatio { get; set; }
    public double MinWorkingDistanceMm { get; set; }
    public double MaxWorkingDistanceMm { get; set; }
    public StructuredLightPattern Pattern { get; set; }
    public int PatternCount { get; set; }
    public double PhaseShift { get; set; }
}

/// <summary>创建/更新电机硬件参数</summary>
public class CreateUpdateMotorParameterInput
{
    [Required]
    public Guid MotorAxisId { get; set; }

    [Required]
    public MotorRole Role { get; set; }

    public int EncoderResolution { get; set; }
    public double GearRatio { get; set; }

    public double HomePosition { get; set; }
    public int HomeDirection { get; set; }
    public double HomingVelocity { get; set; }
    public double HomingAcceleration { get; set; }

    public double SoftLimitMin { get; set; }
    public double SoftLimitMax { get; set; }
}
