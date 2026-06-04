using System.ComponentModel.DataAnnotations;

namespace AuroraStruct3D.Calibration.Dtos;

/// <summary>
/// 保存（创建或更新）标定相机参数的输入 DTO。
/// 后端按 CalibProjectId + CameraDeviceId 执行 Upsert 逻辑。
/// </summary>
public class SaveCalibCameraParamInput
{
    /// <summary>所属标定项目ID</summary>
    [Required]
    public Guid CalibProjectId { get; set; }

    /// <summary>对应相机设备ID</summary>
    [Required]
    public Guid CameraDeviceId { get; set; }

    /// <summary>参数配置名称</summary>
    [Required]
    [MaxLength(CalibConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    /// <summary>参数配置描述</summary>
    [MaxLength(CalibConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>CMOS传感器尺寸描述</summary>
    [MaxLength(CalibConsts.MaxSensorSizeLength)]
    public string? SensorSize { get; set; }

    /// <summary>传感器物理宽度（mm）</summary>
    public decimal? SensorWidthMm { get; set; }

    /// <summary>传感器物理高度（mm）</summary>
    public decimal? SensorHeightMm { get; set; }

    /// <summary>图像宽度（像素）</summary>
    public int? ImageWidthPixels { get; set; }

    /// <summary>图像高度（像素）</summary>
    public int? ImageHeightPixels { get; set; }

    /// <summary>镜头标称焦距（mm）</summary>
    public decimal? LensFocalLength { get; set; }

    /// <summary>最大光圈F值</summary>
    public decimal? MaxAperture { get; set; }

    /// <summary>最小光圈F值</summary>
    public decimal? MinAperture { get; set; }

    /// <summary>当前使用的光圈F值</summary>
    public decimal? CurrentAperture { get; set; }

    /// <summary>最小曝光时间（μs）</summary>
    public int? ExposureTimeMinUs { get; set; }

    /// <summary>最大曝光时间（μs）</summary>
    public int? ExposureTimeMaxUs { get; set; }

    /// <summary>最小增益（dB）</summary>
    public decimal? GainMinDb { get; set; }

    /// <summary>最大增益（dB）</summary>
    public decimal? GainMaxDb { get; set; }
}
