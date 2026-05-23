using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.SerialPorts;

/// <summary>
/// 串口通讯配置聚合根。
/// 代表一个物理 RS485/串口总线的连接参数，可被多个设备（电机轴等）共享引用。
///
/// 数据库表：AbpProSerialPortConfigs
/// </summary>
public class SerialPortConfig : FullAuditedAggregateRoot<Guid>
{
    // ─────────────────────────── 基本信息 ───────────────────────────

    /// <summary>显示名称，便于用户识别（如"电机总线-1"、"相机串口"）</summary>
    public string DisplayName { get; private set; } = null!;

    /// <summary>描述/备注</summary>
    public string? Description { get; private set; }

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; private set; }

    // ─────────────────────────── 串口参数 ───────────────────────────

    /// <summary>
    /// 系统串口名称。
    /// Windows 格式：COM1、COM2…；Linux 格式：/dev/ttyS0、/dev/ttyUSB0…
    /// </summary>
    public string PortName { get; private set; } = null!;

    /// <summary>波特率（如 9600、19200、115200）</summary>
    public int BaudRate { get; private set; }

    /// <summary>数据位（通常为 7 或 8）</summary>
    public int DataBits { get; private set; }

    /// <summary>奇偶校验位</summary>
    public SerialPortParity Parity { get; private set; }

    /// <summary>停止位</summary>
    public SerialPortStopBits StopBits { get; private set; }

    /// <summary>流控制（RS485 半双工通常为 None）</summary>
    public SerialPortHandshake Handshake { get; private set; }

    // ─────────────────────────── 构造函数 ───────────────────────────

    /// <summary>EF Core 所需的无参构造函数</summary>
    protected SerialPortConfig() { }

    /// <summary>
    /// 创建串口通讯配置
    /// </summary>
    /// <param name="id">聚合根 ID</param>
    /// <param name="displayName">显示名称</param>
    /// <param name="portName">系统串口名称</param>
    /// <param name="baudRate">波特率</param>
    /// <param name="dataBits">数据位（默认 8）</param>
    /// <param name="parity">奇偶校验位（默认无校验）</param>
    /// <param name="stopBits">停止位（默认 1 位）</param>
    /// <param name="handshake">流控制（默认无流控）</param>
    public SerialPortConfig(
        Guid id,
        string displayName,
        string portName,
        int baudRate,
        int dataBits = 8,
        SerialPortParity parity = SerialPortParity.None,
        SerialPortStopBits stopBits = SerialPortStopBits.One,
        SerialPortHandshake handshake = SerialPortHandshake.None
    )
        : base(id)
    {
        SetDisplayName(displayName);
        SetPortName(portName);
        SetParameters(baudRate, dataBits, parity, stopBits, handshake);
        IsEnabled = true;
    }

    // ─────────────────────────── 领域方法 ───────────────────────────

    /// <summary>设置显示名称</summary>
    public SerialPortConfig SetDisplayName(string displayName)
    {
        Check.NotNullOrWhiteSpace(
            displayName,
            nameof(displayName),
            SerialPortConsts.MaxDisplayNameLength
        );
        DisplayName = displayName;
        return this;
    }

    /// <summary>设置系统串口名称（如 COM3、/dev/ttyS6）</summary>
    public SerialPortConfig SetPortName(string portName)
    {
        Check.NotNullOrWhiteSpace(
            portName,
            nameof(portName),
            SerialPortConsts.MaxPortNameLength
        );
        PortName = portName;
        return this;
    }

    /// <summary>设置串口通讯参数（波特率、数据位、校验、停止位、流控）</summary>
    public SerialPortConfig SetParameters(
        int baudRate,
        int dataBits = 8,
        SerialPortParity parity = SerialPortParity.None,
        SerialPortStopBits stopBits = SerialPortStopBits.One,
        SerialPortHandshake handshake = SerialPortHandshake.None
    )
    {
        if (baudRate != 0 && !SerialPortConsts.SupportedBaudRates.Contains(baudRate))
        {
            throw new BusinessException("SerialPorts:UnsupportedBaudRate")
                .WithData("BaudRate", baudRate);
        }
        Check.Range(dataBits, nameof(dataBits), 5, 8);

        BaudRate = baudRate;
        DataBits = dataBits;
        Parity = parity;
        StopBits = stopBits;
        Handshake = handshake;
        return this;
    }

    /// <summary>设置描述</summary>
    public SerialPortConfig SetDescription(string? description)
    {
        if (description != null)
        {
            Check.Length(description, nameof(description), SerialPortConsts.MaxDescriptionLength);
        }
        Description = description;
        return this;
    }

    /// <summary>切换启用状态</summary>
    public SerialPortConfig SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
        return this;
    }
}
