using System.ComponentModel.DataAnnotations;
using AuroraStruct3D.Projectors;
using Volo.Abp.Application.Dtos;

namespace AuroraStruct3D.Projectors.Dtos;

/// <summary>
/// 创建 TCP 连接方式投影仪设备请求 DTO
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
/// 创建 USB HID 连接方式投影仪设备请求 DTO
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
/// 更新投影仪设备基本信息请求 DTO
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

    /// <summary>USB 光机用户寄存器 0 中预先写入的稳定身份（1~255）。</summary>
    [Range(1, 255)]
    public int? DeviceHardwareId { get; set; }
}

/// <summary>
/// 投影仪列表查询请求 DTO
/// </summary>
public class GetProjectorListDto : PagedAndSortedResultRequestDto
{
    /// <summary>名称关键字过滤</summary>
    public string? Filter { get; set; }

    /// <summary>是否仅查询启用的设备</summary>
    public bool? IsEnabled { get; set; }
}

/// <summary>
/// 投影仪操作日志分页查询请求 DTO
/// </summary>
public class GetProjectorLogListDto : PagedAndSortedResultRequestDto
{
    /// <summary>投影仪设备 ID（不传则查所有设备）</summary>
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
/// 手动控制投影仪命令请求 DTO
/// </summary>
public class ProjectorControlDto
{
    /// <summary>目标投影仪设备 ID</summary>
    [Required]
    public Guid ProjectorDeviceId { get; set; }
}

/// <summary>
/// 设置亮度命令请求 DTO
/// </summary>
public class SetProjectorLightDto : ProjectorControlDto
{
    /// <summary>亮度值（0~175）</summary>
    [Range(0, 175)]
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
/// 切换到下一张条纹请求 DTO（单帧触发模式 B 2）。
/// </summary>
public class NextProjectorFrameDto : ProjectorControlDto { }

/// <summary>
/// 写寄存器请求 DTO
/// </summary>
public class WriteProjectorRegisterDto : ProjectorControlDto
{
    /// <summary>寄存器地址</summary>
    [Range(0, 200)]
    public int Address { get; set; }

    /// <summary>写入值</summary>
    [Range(0, 255)]
    public int Value { get; set; }
}

/// <summary>
/// 投影仪像素分辨率响应 DTO
/// </summary>
public class ProjectorPixelResolutionDto
{
    /// <summary>投影宽度（像素）</summary>
    public int WidthPixels { get; set; }

    /// <summary>像素模式描述（例如 "1280 Pixel Mode"）</summary>
    public string PixelMode { get; set; } = string.Empty;
}

/// <summary>
/// 单张条纹预览图 DTO。
/// </summary>
public class FringePreviewImageDto
{
    /// <summary>图像序号（从 0 开始）</summary>
    public int Index { get; set; }

    /// <summary>图像标签</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// 预览像素数据。
    /// 竖条纹时长度=WidthPixels，横条纹时长度=HeightPixels。
    /// JSON 序列化时将输出为 Base64 字符串。
    /// </summary>
    public byte[] Pixels { get; set; } = [];
}

/// <summary>
/// 投影仪条纹下载状态 DTO。
/// </summary>
public class ProjectorFringeDownloadStatusDto
{
    /// <summary>投影仪设备 ID</summary>
    public Guid ProjectorId { get; set; }

    /// <summary>状态：Idle / Running / Completed / Failed</summary>
    public string Status { get; set; } = "Idle";

    /// <summary>进度百分比（0~100）</summary>
    public int Progress { get; set; }

    /// <summary>失败时的错误信息</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 下载条纹图案到光机 Flash 请求 DTO
/// </summary>
public class DownloadFringePatternInputDto
{
    /// <summary>目标投影仪设备 ID</summary>
    [Required]
    public Guid ProjectorId { get; set; }

    /// <summary>条纹方向：horizontal=横条纹，vertical=竖条纹</summary>
    [Required]
    public string FringeMode { get; set; } = "horizontal";

    /// <summary>条纹类型：bw=黑白（首色黑），wb=白黑（首色白）</summary>
    [Required]
    public string FringeType { get; set; } = "bw";

    /// <summary>投影宽度（像素）</summary>
    [Range(1, 4096)]
    public int WidthPixels { get; set; }

    /// <summary>投影高度（像素）</summary>
    [Range(1, 4096)]
    public int HeightPixels { get; set; }

    /// <summary>兼容旧接口；固定条纹配置下不参与图像计算</summary>
    [Range(1, 100)]
    public int PeriodCount { get; set; }

    /// <summary>兼容旧接口；固定条纹配置下不参与图像计算</summary>
    [Range(1, 64)]
    public int ImageCount { get; set; }

    /// <summary>兼容旧接口；固定条纹配置下不参与计算，互补图直接逐像素取反</summary>
    [Range(1, 4096)]
    public int PhaseShift { get; set; }

    /// <summary>
    /// 横条纹帧不足 1280 列时，剩余黑色填充区域的位置。
    /// 前端实际投影宽度通常为 720，地址空间为 1280，因此横条纹需补 560 列黑色。
    /// start=填充放在条纹数据前面（左侧），end=填充放在条纹数据后面（右侧，默认）。
    /// </summary>
    [MaxLength(16)]
    public string HorizontalPaddingPosition { get; set; } = "end";
}
