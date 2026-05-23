namespace AuroraStruct3D.SerialPorts;

/// <summary>
/// 串口通讯配置模块常量定义
/// </summary>
public static class SerialPortConsts
{
    /// <summary>显示名称最大长度（如"电机总线-1"）</summary>
    public const int MaxDisplayNameLength = 64;

    /// <summary>系统串口名最大长度（如 /dev/ttyS6、COM1）</summary>
    public const int MaxPortNameLength = 64;

    /// <summary>描述备注最大长度</summary>
    public const int MaxDescriptionLength = 512;

    /// <summary>数据库表名前缀</summary>
    public const string DbTablePrefix = "AbpPro";

    /// <summary>支持的常用串口波特率，覆盖 110 到 921600</summary>
    public static readonly int[] SupportedBaudRates =
    [
        110,
        300,
        600,
        1200,
        2400,
        4800,
        9600,
        14400,
        19200,
        38400,
        57600,
        115200,
        128000,
        230400,
        256000,
        460800,
        921600,
    ];
}

/// <summary>
/// 串口奇偶校验位类型。
/// 枚举值与 <see cref="System.IO.Ports.Parity"/> 完全对齐，可安全强转。
/// </summary>
public enum SerialPortParity
{
    /// <summary>无校验</summary>
    None = 0,

    /// <summary>奇校验</summary>
    Odd = 1,

    /// <summary>偶校验</summary>
    Even = 2,

    /// <summary>标记校验（始终为1）</summary>
    Mark = 3,

    /// <summary>空位校验（始终为0）</summary>
    Space = 4,
}

/// <summary>
/// 串口停止位类型。
/// 枚举值与 <see cref="System.IO.Ports.StopBits"/> 完全对齐，可安全强转。
/// </summary>
public enum SerialPortStopBits
{
    /// <summary>无停止位（不推荐）</summary>
    None = 0,

    /// <summary>1位停止位（最常用）</summary>
    One = 1,

    /// <summary>2位停止位</summary>
    Two = 2,

    /// <summary>1.5位停止位</summary>
    OnePointFive = 3,
}

/// <summary>
/// 串口流控制（握手）类型。
/// 枚举值与 <see cref="System.IO.Ports.Handshake"/> 完全对齐，可安全强转。
/// RS485 半双工总线通常使用 <see cref="None"/>。
/// </summary>
public enum SerialPortHandshake
{
    /// <summary>无流控（RS485标准用法）</summary>
    None = 0,

    /// <summary>软件流控 XOn/XOff</summary>
    XOnXOff = 1,

    /// <summary>硬件流控 RTS/CTS</summary>
    RequestToSend = 2,

    /// <summary>软硬件组合流控</summary>
    RequestToSendXOnXOff = 3,
}
