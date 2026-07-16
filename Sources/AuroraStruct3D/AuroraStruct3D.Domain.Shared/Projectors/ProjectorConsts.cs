namespace AuroraStruct3D.Projectors;

/// <summary>
/// DLP 结构光投影仪模块常量定义
/// </summary>
public static class ProjectorConsts
{
    /// <summary>投影仪名称最大长度（如"主投影仪"、"备份投影仪"）</summary>
    public const int MaxNameLength = 128;

    /// <summary>描述最大长度</summary>
    public const int MaxDescriptionLength = 512;

    /// <summary>IP 地址最大长度（IPv4 字符串，最长 15 字符；预留 IPv6）</summary>
    public const int MaxIpAddressLength = 64;

    /// <summary>固件版本字符串最大长度</summary>
    public const int MaxFirmwareVersionLength = 64;

    /// <summary>操作日志消息最大长度</summary>
    public const int MaxLogMessageLength = 512;

    /// <summary>HID 设备路径最大长度（如 /dev/hidraw0、\\?\HID#...）</summary>
    public const int MaxHidDevicePathLength = 256;

    /// <summary>腾聚 TJ 系列投影仪固定 USB HID 厂商 ID（STM32 USB HID 芯片，硬件固定，不可修改）</summary>
    public const int HidVendorId = 0x0483;

    /// <summary>腾聚 TJ 系列投影仪固定 USB HID 产品 ID（STM32 USB HID 芯片，硬件固定，不可修改）</summary>
    public const int HidProductId = 0x5750;

    /// <summary>默认连接超时时间（毫秒）</summary>
    public const int DefaultConnectTimeoutMs = 5000;

    /// <summary>数据库表名前缀</summary>
    public const string DbTablePrefix = "AbpPro";
}

/// <summary>
/// 投影仪物理连接方式
/// </summary>
public enum ProjectorConnectionType
{
    /// <summary>TCP/IP 网络连接（腾聚 TJ 协议默认端口 1234）</summary>
    Tcp = 0,

    /// <summary>USB HID 连接（Megawin EasyPOD 芯片，VID=0x0E6A，PID=0x0317，跨平台 Windows + Linux）</summary>
    UsbHid = 1,
}

/// <summary>
/// 投影仪当前连接状态
/// </summary>
public enum ProjectorConnectionStatus
{
    /// <summary>未知 / 从未连接</summary>
    Unknown = 0,

    /// <summary>已断开连接</summary>
    Disconnected = 1,

    /// <summary>已连接</summary>
    Connected = 2,

    /// <summary>连接失败（超时或网络不通）</summary>
    ConnectionFailed = 3,
}

/// <summary>
/// 投影仪灯（LED）状态
/// </summary>
public enum ProjectorLedStatus
{
    /// <summary>未知</summary>
    Unknown = 0,

    /// <summary>关闭</summary>
    Off = 1,

    /// <summary>开启</summary>
    On = 2,
}

/// <summary>
/// 投影仪操作日志类型
/// </summary>
public enum ProjectorOperationType
{
    /// <summary>连接</summary>
    Connect = 0,

    /// <summary>断开连接</summary>
    Disconnect = 1,

    /// <summary>开灯</summary>
    LedOn = 2,

    /// <summary>关灯</summary>
    LedOff = 3,

    /// <summary>设置亮度</summary>
    SetLight = 4,

    /// <summary>设置显示模式</summary>
    SetDisplayMode = 5,

    /// <summary>设置颜色</summary>
    SetColor = 6,

    /// <summary>触发条纹投影</summary>
    TriggerOnce = 7,

    /// <summary>发送原始命令</summary>
    SendRawCommand = 8,

    /// <summary>查询状态</summary>
    QueryStatus = 9,

    /// <summary>设置图像翻转</summary>
    SetFlip = 10,

    /// <summary>设置触发模式</summary>
    SetTriggerMode = 11,

    /// <summary>设置开机默认图案</summary>
    SetBootImage = 12,

    /// <summary>设置棋盘格像素数</summary>
    SetCheckerboardPixel = 13,

    /// <summary>设置 RGB 颜色（彩光模式）</summary>
    SetRgbColor = 14,

    /// <summary>软复位</summary>
    SoftReset = 15,

    /// <summary>保存参数到设备内存</summary>
    SaveParams = 16,

    /// <summary>读取寄存器</summary>
    ReadRegister = 17,

    /// <summary>写入寄存器</summary>
    WriteRegister = 18,
}

/// <summary>
/// 图像翻转模式
/// </summary>
public enum ProjectorFlipMode
{
    /// <summary>不翻转（默认）</summary>
    None = 0,

    /// <summary>X 轴翻转</summary>
    FlipX = 1,

    /// <summary>Y 轴翻转</summary>
    FlipY = 2,

    /// <summary>XY 翻转</summary>
    FlipXY = 3,
}

/// <summary>
/// 触发模式
/// </summary>
public enum ProjectorTriggerMode
{
    /// <summary>普通触发模式 — 收到 T 指令后直接投射一组条纹</summary>
    Normal = 0,

    /// <summary>循环触发模式 — 收到 T 指令后循环投射当前组条纹</summary>
    Loop = 1,

    /// <summary>单帧触发模式 — 收到 T 后投射一张并保持，收到 N 切换到下一张</summary>
    SingleFrame = 2,
}

/// <summary>
/// 开机默认图案
/// </summary>
public enum ProjectorBootImage
{
    /// <summary>黑图像</summary>
    Black = 0,

    /// <summary>白图像</summary>
    White = 1,

    /// <summary>十字图像</summary>
    Cross = 2,

    /// <summary>棋盘格图像</summary>
    Checkerboard = 3,

    /// <summary>模式 6（S6）</summary>
    Internal1 = 6,

    /// <summary>模式 7（S7）</summary>
    Internal2 = 7,
}

/// <summary>
/// 投影仪内容显示模式
/// </summary>
public enum ProjectorDisplayMode : byte
{
    /// <summary>黑屏</summary>
    Black = 0,

    /// <summary>白屏</summary>
    White = 1,

    /// <summary>十字线</summary>
    Cross = 2,

    /// <summary>棋盘格</summary>
    Checkerboard = 3,

    /// <summary>模式 6（S6）</summary>
    Internal1 = 6,

    /// <summary>模式 7（S7）</summary>
    Internal2 = 7,
}

/// <summary>
/// 投影仪颜色（仅多光谱结构光投影仪支持）
/// </summary>
public enum ProjectorColor : byte
{
    /// <summary>红色</summary>
    Red = 0,

    /// <summary>绿色</summary>
    Green = 1,

    /// <summary>蓝色</summary>
    Blue = 2,

    /// <summary>白色（全色）</summary>
    White = 3,

    /// <summary>彩光（Aura Sync RGB 模式）</summary>
    AuraSync = 4,
}
