using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Projectors;

/// <summary>
/// DLP 结构光投影机设备聚合根。
/// 存储投影机的网络连接配置、当前运行状态及参数快照。
/// 一台物理投影机对应一条记录。
///
/// 数据库表：AbpProProjectors
/// 通信协议：TCP ASCII，端口 1234（腾聚 TJ 系列协议）
/// </summary>
public class ProjectorDevice : FullAuditedAggregateRoot<Guid>
{
    // ─────────────────────────── 基本信息 ───────────────────────────

    /// <summary>投影机名称（如"主投影机"、"A工位投影机"）</summary>
    public string Name { get; private set; } = null!;

    /// <summary>显示序号，用于排序（0=第一台）</summary>
    public int DeviceIndex { get; private set; }

    /// <summary>描述/备注</summary>
    public string? Description { get; private set; }

    /// <summary>是否启用（停用时不参与扫描和连接）</summary>
    public bool IsEnabled { get; private set; }

    // ─────────────────────────── 连接配置 ───────────────────────────

    /// <summary>物理连接方式（TCP 或 USB HID）</summary>
    public ProjectorConnectionType ConnectionType { get; private set; }

    /// <summary>投影机 IP 地址（仅 TCP 模式有效，如 192.168.100.100）</summary>
    public string? IpAddress { get; private set; }

    /// <summary>TCP 端口号（仅 TCP 模式有效，腾聚 TJ 系列固定为 1234）</summary>
    public int TcpPort { get; private set; }

    /// <summary>USB HID 厂商 ID（仅 USB HID 模式有效，腾聚 TJ 默认 0x0E6A = 3690）</summary>
    public int HidVendorId { get; private set; }

    /// <summary>USB HID 产品 ID（仅 USB HID 模式有效，腾聚 TJ 默认 0x0317 = 791）</summary>
    public int HidProductId { get; private set; }

    /// <summary>USB HID 设备索引（同一 VID/PID 多台设备时用于区分，从 0 开始）</summary>
    public int HidDeviceIndex { get; private set; }

    /// <summary>连接超时时间（毫秒，默认 5000）</summary>
    public int ConnectTimeoutMs { get; private set; }

    // ─────────────────────────── 设备信息（连接后查询填充） ─────────────────────

    /// <summary>固件版本字符串（连接后由 "v\r\n" 命令查询，可为 null）</summary>
    public string? FirmwareVersion { get; private set; }

    /// <summary>设备标志字节 ID（由 "pr 0\r\n" 命令查询，-1 表示未知）</summary>
    public int DeviceHardwareId { get; private set; }

    // ─────────────────────────── 运行状态快照（最后已知状态）──────────────────────

    /// <summary>当前连接状态</summary>
    public ProjectorConnectionStatus ConnectionStatus { get; private set; }

    /// <summary>LED 灯状态（最后已知）</summary>
    public ProjectorLedStatus LedStatus { get; private set; }

    /// <summary>最后设置的亮度值（10~200，0 表示未设置）</summary>
    public byte LastLightValue { get; private set; }

    /// <summary>最后设置的显示模式（0=黑屏,1=白屏,2=十字,3=棋盘）</summary>
    public byte LastDisplayMode { get; private set; }

    /// <summary>最后一次成功通信时间</summary>
    public DateTime? LastCommunicationAt { get; private set; }

    /// <summary>最后一次连接时间</summary>
    public DateTime? LastConnectedAt { get; private set; }

    /// <summary>最后一次断开时间</summary>
    public DateTime? LastDisconnectedAt { get; private set; }

    // ─────────────────────────── 操作日志集合（关联） ───────────────────────────

    /// <summary>该投影机的操作历史日志</summary>
    public ICollection<ProjectorOperationLog> OperationLogs { get; private set; } =
        new List<ProjectorOperationLog>();

    // ─────────────────────────── 构造函数 ───────────────────────────

    /// <summary>
    /// EF Core 专用无参构造函数
    /// </summary>
    protected ProjectorDevice() { }

    /// <summary>
    /// 创建 TCP 连接方式的投影机设备
    /// </summary>
    /// <param name="id">主键</param>
    /// <param name="name">设备名称</param>
    /// <param name="deviceIndex">序号</param>
    /// <param name="ipAddress">IP 地址</param>
    /// <param name="tcpPort">TCP 端口（默认 1234）</param>
    /// <param name="connectTimeoutMs">连接超时（毫秒，默认 5000）</param>
    public ProjectorDevice(
        Guid id,
        string name,
        int deviceIndex,
        string ipAddress,
        int tcpPort = 1234,
        int connectTimeoutMs = 5000
    )
        : base(id)
    {
        SetName(name);
        DeviceIndex = deviceIndex;
        ConnectionType = ProjectorConnectionType.Tcp;
        SetIpAddress(ipAddress);
        TcpPort = tcpPort;
        ConnectTimeoutMs = connectTimeoutMs;

        // 默认状态
        ConnectionStatus = ProjectorConnectionStatus.Unknown;
        LedStatus = ProjectorLedStatus.Unknown;
        IsEnabled = true;
        DeviceHardwareId = -1;
    }

    /// <summary>
    /// 创建 USB HID 连接方式的投影机设备（Megawin EasyPOD 芯片）
    /// </summary>
    /// <param name="id">主键</param>
    /// <param name="name">设备名称</param>
    /// <param name="deviceIndex">序号</param>
    /// <param name="hidVendorId">HID 厂商 ID（默认 0x0E6A 腾聚）</param>
    /// <param name="hidProductId">HID 产品 ID（默认 0x0317）</param>
    /// <param name="hidDeviceIndex">HID 设备索引（多台时区分，从 0 开始）</param>
    /// <param name="connectTimeoutMs">连接超时（毫秒，默认 5000）</param>
    public ProjectorDevice(
        Guid id,
        string name,
        int deviceIndex,
        int hidVendorId,
        int hidProductId,
        int hidDeviceIndex = 0,
        int connectTimeoutMs = 5000
    )
        : base(id)
    {
        SetName(name);
        DeviceIndex = deviceIndex;
        ConnectionType = ProjectorConnectionType.UsbHid;
        HidVendorId = hidVendorId;
        HidProductId = hidProductId;
        HidDeviceIndex = hidDeviceIndex;
        ConnectTimeoutMs = connectTimeoutMs;

        // 默认状态
        ConnectionStatus = ProjectorConnectionStatus.Unknown;
        LedStatus = ProjectorLedStatus.Unknown;
        IsEnabled = true;
        DeviceHardwareId = -1;
    }

    // ─────────────────────────── 方法 ───────────────────────────

    /// <summary>设置设备名称</summary>
    public ProjectorDevice SetName(string name)
    {
        Name = Check.NotNullOrWhiteSpace(
            name,
            nameof(name),
            maxLength: ProjectorConsts.MaxNameLength
        );
        return this;
    }

    /// <summary>设置 IP 地址（TCP 模式）</summary>
    public ProjectorDevice SetIpAddress(string ipAddress)
    {
        IpAddress = Check.NotNullOrWhiteSpace(
            ipAddress,
            nameof(ipAddress),
            maxLength: ProjectorConsts.MaxIpAddressLength
        );
        return this;
    }

    /// <summary>设置 USB HID 设备索引（USB HID 模式）</summary>
    public ProjectorDevice SetHidDeviceIndex(int hidDeviceIndex)
    {
        HidDeviceIndex = hidDeviceIndex;
        return this;
    }

    /// <summary>设置描述</summary>
    public ProjectorDevice SetDescription(string? description)
    {
        Description = description;
        return this;
    }

    /// <summary>更新连接状态</summary>
    public ProjectorDevice UpdateConnectionStatus(
        ProjectorConnectionStatus status,
        DateTime? connectedAt = null,
        DateTime? disconnectedAt = null
    )
    {
        ConnectionStatus = status;
        if (connectedAt.HasValue)
        {
            LastConnectedAt = connectedAt.Value;
        }

        if (disconnectedAt.HasValue)
        {
            LastDisconnectedAt = disconnectedAt.Value;
        }

        return this;
    }

    /// <summary>更新设备信息（连接后从设备查询）</summary>
    public ProjectorDevice UpdateDeviceInfo(string? firmwareVersion, int hardwareId)
    {
        FirmwareVersion = firmwareVersion;
        DeviceHardwareId = hardwareId;
        LastCommunicationAt = DateTime.UtcNow;
        return this;
    }

    /// <summary>更新 LED 状态</summary>
    public ProjectorDevice UpdateLedStatus(ProjectorLedStatus status)
    {
        LedStatus = status;
        LastCommunicationAt = DateTime.UtcNow;
        return this;
    }

    /// <summary>更新亮度设置</summary>
    public ProjectorDevice UpdateLightValue(byte lightValue)
    {
        LastLightValue = lightValue;
        LastCommunicationAt = DateTime.UtcNow;
        return this;
    }

    /// <summary>更新显示模式</summary>
    public ProjectorDevice UpdateDisplayMode(byte displayMode)
    {
        LastDisplayMode = displayMode;
        LastCommunicationAt = DateTime.UtcNow;
        return this;
    }

    /// <summary>启用设备</summary>
    public ProjectorDevice Enable()
    {
        IsEnabled = true;
        return this;
    }

    /// <summary>停用设备</summary>
    public ProjectorDevice Disable()
    {
        IsEnabled = false;
        return this;
    }
}
