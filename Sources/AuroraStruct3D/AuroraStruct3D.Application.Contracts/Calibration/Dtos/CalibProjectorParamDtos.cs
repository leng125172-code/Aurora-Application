using System.ComponentModel.DataAnnotations;

namespace AuroraStruct3D.Calibration.Dtos;

/// <summary>
/// 投影仪参数配置 DTO（Step3 投影仪参数配置页回显用）
/// </summary>
public class CalibProjectorParamDto
{
    /// <summary>实体ID</summary>
    public Guid Id { get; set; }

    /// <summary>所属标定项目ID</summary>
    public Guid CalibProjectId { get; set; }

    /// <summary>对应的投射器设备ID</summary>
    public Guid ProjectorDeviceId { get; set; }

    /// <summary>参数配置名称</summary>
    public string Name { get; set; } = null!;

    /// <summary>参数配置描述</summary>
    public string? Description { get; set; }

    /// <summary>投射器分辨率宽度（像素，从投影仪读取）</summary>
    public int ResolutionWidth { get; set; }

    /// <summary>投射器分辨率高度（像素，用户输入）</summary>
    public int ResolutionHeight { get; set; }

    /// <summary>条纹周期数</summary>
    public int PeriodCount { get; set; }

    /// <summary>条纹类型：bw=黑白，wb=白黑</summary>
    public string FringeType { get; set; } = "bw";

    public byte DarkLevel { get; set; } = 24;

    public byte BrightLevel { get; set; } = 220;

    /// <summary>每个方向的相移图像数量</summary>
    public int PatternCount { get; set; }

    /// <summary>相位偏移量</summary>
    public decimal? PhaseShift { get; set; }

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; }

    /// <summary>创建时间</summary>
    public DateTime CreationTime { get; set; }

    /// <summary>最后修改时间</summary>
    public DateTime? LastModificationTime { get; set; }
}

/// <summary>
/// 保存投影仪参数配置输入（Step3 投影仪参数配置页保存用，按 CalibProjectId 执行 Upsert）
/// </summary>
public class SaveCalibProjectorParamInput
{
    /// <summary>所属标定项目ID</summary>
    [Required]
    public Guid CalibProjectId { get; set; }

    /// <summary>对应的投射器设备ID</summary>
    [Required]
    public Guid ProjectorDeviceId { get; set; }

    /// <summary>投射器分辨率宽度（像素，从投影仪读取后传入）</summary>
    public int ResolutionWidth { get; set; }

    /// <summary>投射器分辨率高度（像素，用户输入）</summary>
    [Range(1, 10000)]
    public int ResolutionHeight { get; set; }

    /// <summary>条纹周期数</summary>
    [Range(1, 1000)]
    public int PeriodCount { get; set; }

    /// <summary>条纹类型：bw=黑白（首色黑），wb=白黑（首色白）</summary>
    [Required]
    [MaxLength(8)]
    public string FringeType { get; set; } = "bw";

    [Range(0, 254)]
    public byte DarkLevel { get; set; } = 24;

    [Range(1, 255)]
    public byte BrightLevel { get; set; } = 220;

    /// <summary>每个方向的相移图像数量；扫描总帧数为其两倍</summary>
    [Range(3, 64)]
    public int PatternCount { get; set; }

    /// <summary>相位偏移量</summary>
    [Range(0, 1000)]
    public decimal? PhaseShift { get; set; }
}
