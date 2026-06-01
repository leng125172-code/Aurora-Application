using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// 相机硬件参数配置实体（Step 5）。
/// 存储用于标定的相机光学和传感器参数，
/// 可按相机型号分类保存为模板以便复用。
///
/// 数据库表：AbpProCalibCameraParams
/// </summary>
public class CalibCameraParam : FullAuditedEntity<Guid>
{
    /// <summary>所属标定项目ID（关联AbpProCalibProjects）</summary>
    public Guid CalibProjectId { get; private set; }

    /// <summary>对应的相机设备ID（关联AbpProCameraDevices）</summary>
    public Guid CameraDeviceId { get; private set; }

    /// <summary>参数配置名称</summary>
    public string Name { get; private set; } = null!;

    /// <summary>参数配置描述</summary>
    public string? Description { get; private set; }

    /// <summary>CMOS传感器尺寸描述（如"1/2.3英寸"）</summary>
    public string SensorSize { get; private set; } = null!;

    /// <summary>传感器物理宽度（mm）</summary>
    public decimal SensorWidthMm { get; private set; }

    /// <summary>传感器物理高度（mm）</summary>
    public decimal SensorHeightMm { get; private set; }

    /// <summary>图像宽度（像素）</summary>
    public int ImageWidthPixels { get; private set; }

    /// <summary>图像高度（像素）</summary>
    public int ImageHeightPixels { get; private set; }

    /// <summary>像素物理尺寸（μm/像素，由传感器尺寸和分辨率自动计算）</summary>
    public decimal PixelSizeUm { get; private set; }

    /// <summary>镜头标称焦距（mm）</summary>
    public decimal LensFocalLength { get; private set; }

    /// <summary>最大光圈F值</summary>
    public decimal MaxAperture { get; private set; }

    /// <summary>最小光圈F值</summary>
    public decimal MinAperture { get; private set; }

    /// <summary>当前使用的光圈F值</summary>
    public decimal CurrentAperture { get; private set; }

    /// <summary>最小曝光时间（μs）</summary>
    public int ExposureTimeMinUs { get; private set; }

    /// <summary>最大曝光时间（μs）</summary>
    public int ExposureTimeMaxUs { get; private set; }

    /// <summary>最小增益（dB）</summary>
    public decimal GainMinDb { get; private set; }

    /// <summary>最大增益（dB）</summary>
    public decimal GainMaxDb { get; private set; }

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>是否已保存为模板</summary>
    public bool IsTemplateMode { get; private set; }

    /// <summary>模板分类（按相机型号分类，仅模板模式有效）</summary>
    public string? TemplateCategory { get; private set; }

    // EF Core 所需的无参构造函数
    protected CalibCameraParam() { }

    /// <summary>
    /// 创建相机标定参数
    /// </summary>
    /// <param name="id">实体ID</param>
    /// <param name="calibProjectId">所属标定项目ID</param>
    /// <param name="cameraDeviceId">相机设备ID</param>
    /// <param name="name">参数名称</param>
    public CalibCameraParam(Guid id, Guid calibProjectId, Guid cameraDeviceId, string name)
        : base(id)
    {
        CalibProjectId = calibProjectId;
        CameraDeviceId = cameraDeviceId;
        SetName(name);
        SensorSize = string.Empty;
        IsEnabled = true;
        IsTemplateMode = false;
    }

    /// <summary>设置参数名称</summary>
    public CalibCameraParam SetName(string name)
    {
        Check.NotNullOrWhiteSpace(name, nameof(name), CalibConsts.MaxNameLength);
        Name = name;
        return this;
    }

    /// <summary>设置描述</summary>
    public CalibCameraParam SetDescription(string? description)
    {
        if (description != null)
        {
            Check.Length(description, nameof(description), CalibConsts.MaxDescriptionLength);
        }
        Description = description;
        return this;
    }

    /// <summary>
    /// 更新传感器与分辨率参数，并自动计算像素物理尺寸
    /// </summary>
    public CalibCameraParam SetSensorParams(
        string sensorSize,
        decimal sensorWidthMm,
        decimal sensorHeightMm,
        int imageWidthPixels,
        int imageHeightPixels
    )
    {
        Check.NotNullOrWhiteSpace(sensorSize, nameof(sensorSize), CalibConsts.MaxSensorSizeLength);
        SensorSize = sensorSize;
        SensorWidthMm = sensorWidthMm;
        SensorHeightMm = sensorHeightMm;
        ImageWidthPixels = imageWidthPixels;
        ImageHeightPixels = imageHeightPixels;

        // 按宽边计算像素物理尺寸（mm → μm 换算：×1000）
        PixelSizeUm =
            imageWidthPixels > 0 ? Math.Round(sensorWidthMm * 1000m / imageWidthPixels, 4) : 0m;
        return this;
    }

    /// <summary>更新镜头与光圈参数</summary>
    public CalibCameraParam SetLensParams(
        decimal lensFocalLength,
        decimal maxAperture,
        decimal minAperture,
        decimal currentAperture
    )
    {
        LensFocalLength = lensFocalLength;
        MaxAperture = maxAperture;
        MinAperture = minAperture;
        CurrentAperture = currentAperture;
        return this;
    }

    /// <summary>更新曝光和增益范围参数</summary>
    public CalibCameraParam SetExposureGainRange(
        int exposureTimeMinUs,
        int exposureTimeMaxUs,
        decimal gainMinDb,
        decimal gainMaxDb
    )
    {
        ExposureTimeMinUs = exposureTimeMinUs;
        ExposureTimeMaxUs = exposureTimeMaxUs;
        GainMinDb = gainMinDb;
        GainMaxDb = gainMaxDb;
        return this;
    }

    /// <summary>设置模板模式及分类</summary>
    public CalibCameraParam SetTemplateMode(bool isTemplate, string? category = null)
    {
        IsTemplateMode = isTemplate;
        if (category != null)
        {
            Check.Length(category, nameof(category), CalibConsts.MaxTemplateCategoryLength);
        }
        TemplateCategory = category;
        return this;
    }

    /// <summary>启用或禁用</summary>
    public CalibCameraParam SetEnabled(bool isEnabled)
    {
        IsEnabled = isEnabled;
        return this;
    }
}
