using AuroraStruct3D.Projectors;
using Volo.Abp.Application.Dtos;

namespace AuroraStruct3D.Projectors.Dtos;

/// <summary>
/// 投影机设备 DTO
/// </summary>
public class ProjectorDeviceDto : FullAuditedEntityDto<Guid>
{
    /// <summary>设备名称</summary>
    public string Name { get; set; } = null!;

    /// <summary>显示序号</summary>
    public int DeviceIndex { get; set; }

    /// <summary>描述</summary>
    public string? Description { get; set; }

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; }

    // ─── 连接配置 ──────────────────────────────────────────────────────

    /// <summary>物理连接方式（Tcp=0, UsbHid=1）</summary>
    public ProjectorConnectionType ConnectionType { get; set; }

    /// <summary>IP 地址（TCP 模式）</summary>
    public string? IpAddress { get; set; }

    /// <summary>TCP 端口（TCP 模式）</summary>
    public int TcpPort { get; set; }

    /// <summary>HID 设备索引（VID/PID 为硬件固定值 0x0483/0x5750）</summary>
    public int HidDeviceIndex { get; set; }

    /// <summary>连接超时（毫秒）</summary>
    public int ConnectTimeoutMs { get; set; }

    // ─── 设备信息 ──────────────────────────────────────────────────────

    /// <summary>固件版本（连接后查询）</summary>
    public string? FirmwareVersion { get; set; }

    /// <summary>设备标志字节 ID（-1 表示未知）</summary>
    public int DeviceHardwareId { get; set; }

    // ─── 运行状态快照 ──────────────────────────────────────────────────

    /// <summary>当前连接状态</summary>
    public ProjectorConnectionStatus ConnectionStatus { get; set; }

    /// <summary>连接状态文字</summary>
    public string ConnectionStatusText =>
        ConnectionStatus switch
        {
            ProjectorConnectionStatus.Connected => "已连接",
            ProjectorConnectionStatus.Disconnected => "已断开",
            ProjectorConnectionStatus.ConnectionFailed => "连接失败",
            _ => "未知",
        };

    /// <summary>LED 灯状态</summary>
    public ProjectorLedStatus LedStatus { get; set; }

    /// <summary>LED 灯状态文字</summary>
    public string LedStatusText =>
        LedStatus switch
        {
            ProjectorLedStatus.On => "亮灯",
            ProjectorLedStatus.Off => "灭灯",
            _ => "未知",
        };

    /// <summary>最后亮度值（10~200）</summary>
    public byte LastLightValue { get; set; }

    /// <summary>最后显示模式</summary>
    public byte LastDisplayMode { get; set; }

    /// <summary>最后颜色（多光谱模式）</summary>
    public ProjectorColor LastColor { get; set; }

    /// <summary>棋盘格像素尺寸</summary>
    public int CheckerboardPixelSize { get; set; }

    /// <summary>图像翻转模式</summary>
    public ProjectorFlipMode FlipMode { get; set; }

    /// <summary>触发模式</summary>
    public ProjectorTriggerMode TriggerMode { get; set; }

    /// <summary>开机默认图案</summary>
    public ProjectorBootImage BootImage { get; set; }

    /// <summary>LED RGB 红色分量</summary>
    public byte LedRgbR { get; set; }

    /// <summary>LED RGB 绿色分量</summary>
    public byte LedRgbG { get; set; }

    /// <summary>LED RGB 蓝色分量</summary>
    public byte LedRgbB { get; set; }

    /// <summary>最后通信时间</summary>
    public DateTime? LastCommunicationAt { get; set; }

    /// <summary>最后连接时间</summary>
    public DateTime? LastConnectedAt { get; set; }

    /// <summary>最后断开时间</summary>
    public DateTime? LastDisconnectedAt { get; set; }
}

/// <summary>
/// 投影机操作日志 DTO
/// </summary>
public class ProjectorOperationLogDto : EntityDto<Guid>
{
    /// <summary>投影机设备 ID</summary>
    public Guid ProjectorDeviceId { get; set; }

    /// <summary>操作类型</summary>
    public ProjectorOperationType OperationType { get; set; }

    /// <summary>是否成功</summary>
    public bool IsSuccess { get; set; }

    /// <summary>原始命令（ASCII）</summary>
    public string? RawCommand { get; set; }

    /// <summary>参数摘要</summary>
    public string? ParameterSummary { get; set; }

    /// <summary>错误信息（失败时）</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>往返延迟（毫秒）</summary>
    public int RoundTripMs { get; set; }

    /// <summary>发生时间</summary>
    public DateTime OccurredAt { get; set; }
}
