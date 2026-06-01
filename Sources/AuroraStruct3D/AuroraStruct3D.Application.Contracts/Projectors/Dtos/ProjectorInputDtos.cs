using System.ComponentModel.DataAnnotations;
using AuroraStruct3D.Projectors;
using Volo.Abp.Application.Dtos;

namespace AuroraStruct3D.Projectors.Dtos;

/// <summary>
/// 创建 TCP 连接方式投影机设备请求 DTO
/// </summary>
public class CreateTcpProjectorDeviceDto
{
    /// <summary>设备名称</summary>
    [Required]
    [MaxLength(ProjectorConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    /// <summary>显示序号</summary>
    [Range(0, 63)]
    public int DeviceIndex { get; set; }

    /// <summary>描述</summary>
    [MaxLength(ProjectorConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>IP 地址</summary>
    [Required]
    [MaxLength(ProjectorConsts.MaxIpAddressLength)]
    public string IpAddress { get; set; } = null!;

    /// <summary>TCP 端口（默认 1234）</summary>
    [Range(1, 65535)]
    public int TcpPort { get; set; } = 1234;

    /// <summary>连接超时（毫秒，默认 5000）</summary>
    [Range(500, 30000)]
    public int ConnectTimeoutMs { get; set; } = 5000;

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// 创建 USB HID 连接方式投影机设备请求 DTO
/// </summary>
public class CreateHidProjectorDeviceDto
{
    /// <summary>设备名称</summary>
    [Required]
    [MaxLength(ProjectorConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    /// <summary>显示序号</summary>
    [Range(0, 63)]
    public int DeviceIndex { get; set; }

    /// <summary>描述</summary>
    [MaxLength(ProjectorConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>HID 设备索引（多台同型号时区分，从 0 开始；VID/PID 为硬件固定值 0x0483/0x5750）</summary>
    [Range(0, 15)]
    public int HidDeviceIndex { get; set; }

    /// <summary>连接超时（毫秒，默认 5000）</summary>
    [Range(500, 30000)]
    public int ConnectTimeoutMs { get; set; } = 5000;

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// 更新投影机设备基本信息请求 DTO
/// </summary>
public class UpdateProjectorDeviceDto
{
    /// <summary>设备名称</summary>
    [Required]
    [MaxLength(ProjectorConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    /// <summary>描述</summary>
    [MaxLength(ProjectorConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>连接超时（毫秒）</summary>
    [Range(500, 30000)]
    public int ConnectTimeoutMs { get; set; } = 5000;
}

/// <summary>
/// 投影机列表查询请求 DTO
/// </summary>
public class GetProjectorListDto : PagedAndSortedResultRequestDto
{
    /// <summary>名称关键字过滤</summary>
    public string? Filter { get; set; }

    /// <summary>是否仅查询启用的设备</summary>
    public bool? IsEnabled { get; set; }
}

/// <summary>
/// 投影机操作日志分页查询请求 DTO
/// </summary>
public class GetProjectorLogListDto : PagedAndSortedResultRequestDto
{
    /// <summary>投影机设备 ID（不传则查所有设备）</summary>
    public Guid? ProjectorDeviceId { get; set; }

    /// <summary>操作类型过滤</summary>
    public ProjectorOperationType? OperationType { get; set; }

    /// <summary>是否仅查询失败记录</summary>
    public bool? IsFailedOnly { get; set; }

    /// <summary>开始时间（可选，UTC）</summary>
    public DateTime? StartTime { get; set; }

    /// <summary>结束时间（可选，UTC）</summary>
    public DateTime? EndTime { get; set; }
}

/// <summary>
/// 手动控制投影机命令请求 DTO
/// </summary>
public class ProjectorControlDto
{
    /// <summary>目标投影机设备 ID</summary>
    [Required]
    public Guid ProjectorDeviceId { get; set; }
}

/// <summary>
/// 设置亮度命令请求 DTO
/// </summary>
public class SetProjectorLightDto : ProjectorControlDto
{
    /// <summary>亮度值（10~200）</summary>
    [Range(10, 200)]
    public byte Light { get; set; }
}

/// <summary>
/// 设置显示模式命令请求 DTO
/// </summary>
public class SetProjectorDisplayModeDto : ProjectorControlDto
{
    /// <summary>显示模式</summary>
    public ProjectorDisplayMode Mode { get; set; }
}

/// <summary>
/// 设置颜色命令请求 DTO（多光谱模式）
/// </summary>
public class SetProjectorColorDto : ProjectorControlDto
{
    /// <summary>颜色</summary>
    public ProjectorColor Color { get; set; }
}

/// <summary>
/// 设置图像翻转模式请求 DTO
/// </summary>
public class SetProjectorFlipDto : ProjectorControlDto
{
    /// <summary>翻转模式</summary>
    public ProjectorFlipMode FlipMode { get; set; }
}

/// <summary>
/// 设置触发模式请求 DTO
/// </summary>
public class SetProjectorTriggerModeDto : ProjectorControlDto
{
    /// <summary>触发模式</summary>
    public ProjectorTriggerMode TriggerMode { get; set; }
}

/// <summary>
/// 设置开机图案请求 DTO
/// </summary>
public class SetProjectorBootImageDto : ProjectorControlDto
{
    /// <summary>开机图案</summary>
    public ProjectorBootImage BootImage { get; set; }
}

/// <summary>
/// 设置棋盘格像素尺寸请求 DTO
/// </summary>
public class SetProjectorCheckerboardDto : ProjectorControlDto
{
    /// <summary>像素尺寸（建议范围 5~100）</summary>
    [Range(1, 200)]
    public int PixelSize { get; set; } = 30;
}

/// <summary>
/// 设置 RGB 颜色请求 DTO（彩光模式）。<br/>
/// R/G/B 为标准颜色值 0~255，后端自动换算为 LED 亮度值 0~175 后发送硬件命令。
/// </summary>
public class SetProjectorRgbDto : ProjectorControlDto
{
    /// <summary>红色分量颜色值（0~255，自动换算为 LED 亮度 0~175）</summary>
    public byte R { get; set; }

    /// <summary>绿色分量颜色值（0~255，自动换算为 LED 亮度 0~175）</summary>
    public byte G { get; set; }

    /// <summary>蓝色分量颜色值（0~255，自动换算为 LED 亮度 0~175）</summary>
    public byte B { get; set; }
}

/// <summary>
/// 触发条纹投影请求 DTO
/// </summary>
public class TriggerProjectorDto : ProjectorControlDto
{
    /// <summary>末尾帧灰度值（0=黑，255=白）</summary>
    [Range(0, 255)]
    public byte EndGray { get; set; } = 255;
}

/// <summary>
/// 写寄存器请求 DTO
/// </summary>
public class WriteProjectorRegisterDto : ProjectorControlDto
{
    /// <summary>寄存器地址</summary>
    [Range(0, 255)]
    public int Address { get; set; }

    /// <summary>写入值</summary>
    public int Value { get; set; }
}
