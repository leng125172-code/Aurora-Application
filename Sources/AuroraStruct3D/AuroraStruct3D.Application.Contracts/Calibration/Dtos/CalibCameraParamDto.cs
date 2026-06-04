namespace AuroraStruct3D.Calibration.Dtos;

/// <summary>
/// 标定相机参数输出 DTO
/// </summary>
public class CalibCameraParamDto
{
    /// <summary>主键</summary>
    public Guid Id { get; set; }

    /// <summary>所属标定项目ID</summary>
    public Guid CalibProjectId { get; set; }

    /// <summary>对应相机设备ID</summary>
    public Guid CameraDeviceId { get; set; }

    /// <summary>参数配置名称</summary>
    public string Name { get; set; } = null!;

    /// <summary>参数配置描述</summary>
    public string? Description { get; set; }

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; }

    /// <summary>CMOS传感器尺寸描述（如"1/2.3英寸"）</summary>
    public string? SensorSize { get; set; }

    /// <summary>传感器物理宽度（mm）</summary>
    public decimal? SensorWidthMm { get; set; }

    /// <summary>传感器物理高度（mm）</summary>
    public decimal? SensorHeightMm { get; set; }

    /// <summary>图像宽度（像素）</summary>
    public int? ImageWidthPixels { get; set; }

    /// <summary>图像高度（像素）</summary>
    public int? ImageHeightPixels { get; set; }

    /// <summary>像素物理尺寸（μm/像素）</summary>
    public decimal? PixelSizeUm { get; set; }

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

    /// <summary>创建时间</summary>
    public DateTime CreationTime { get; set; }

    /// <summary>最后修改时间</summary>
    public DateTime? LastModificationTime { get; set; }
}
