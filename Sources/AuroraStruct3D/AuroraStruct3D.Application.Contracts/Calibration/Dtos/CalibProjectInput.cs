using System.ComponentModel.DataAnnotations;
using AuroraStruct3D.Calibration;

namespace AuroraStruct3D.Calibration.Dtos;

/// <summary>
/// 新增标定项目的输入 DTO
/// </summary>
public class CreateCalibProjectInput
{
    /// <summary>项目名称（必填，最长 256 字符）</summary>
    [Required]
    [MaxLength(CalibConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    /// <summary>项目描述（可选，最长 1024 字符）</summary>
    [MaxLength(CalibConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>设备类型（必填）</summary>
    [Required]
    public CalibDeviceType DeviceType { get; set; }

    /// <summary>主相机设备ID（必填类型：2目0光、1目1光、2目1光）</summary>
    public Guid? MainCameraDeviceId { get; set; }

    /// <summary>从相机设备ID（必填类型：2目0光、2目1光）</summary>
    public Guid? SecondaryCameraDeviceId { get; set; }

    /// <summary>主相机角度控制电机轴ID（必填类型：2目0光、1目1光、2目1光）</summary>
    public Guid? MainCameraMotorAxisId { get; set; }

    /// <summary>从相机角度控制电机轴ID（必填类型：2目0光、2目1光）</summary>
    public Guid? SecondaryCameraMotorAxisId { get; set; }

    /// <summary>间距控制电机轴ID（必填类型：2目0光、1目1光、2目1光）</summary>
    public Guid? DistanceMotorAxisId { get; set; }

    /// <summary>主结构光设备ID（单光系列必填）</summary>
    public Guid? BoundProjectorDeviceId { get; set; }
}

/// <summary>
/// 修改标定项目的输入 DTO（仅允许修改名称和描述）
/// </summary>
public class UpdateCalibProjectInput
{
    /// <summary>项目名称（必填，最长 256 字符）</summary>
    [Required]
    [MaxLength(CalibConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    /// <summary>项目描述（可选，最长 1024 字符）</summary>
    [MaxLength(CalibConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>主相机设备ID（必填类型：2目0光、1目1光、2目1光）</summary>
    public Guid? MainCameraDeviceId { get; set; }

    /// <summary>从相机设备ID（必填类型：2目0光、2目1光）</summary>
    public Guid? SecondaryCameraDeviceId { get; set; }

    /// <summary>主相机角度控制电机轴ID（必填类型：2目0光、1目1光、2目1光）</summary>
    public Guid? MainCameraMotorAxisId { get; set; }

    /// <summary>从相机角度控制电机轴ID（必填类型：2目0光、2目1光）</summary>
    public Guid? SecondaryCameraMotorAxisId { get; set; }

    /// <summary>间距控制电机轴ID（必填类型：2目0光、1目1光、2目1光）</summary>
    public Guid? DistanceMotorAxisId { get; set; }

    /// <summary>主结构光设备ID（单光系列必填）</summary>
    public Guid? BoundProjectorDeviceId { get; set; }
}
