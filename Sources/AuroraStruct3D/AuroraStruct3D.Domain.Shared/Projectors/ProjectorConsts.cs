namespace AuroraStruct3D.Projectors;

/// <summary>
/// DLP 结构光投影机模块常量定义
/// </summary>
public static class ProjectorConsts
{
    /// <summary>投影机名称最大长度（如"主投影机"、"备份投影机"）</summary>
    public const int MaxNameLength = 128;

    /// <summary>描述最大长度</summary>
    public const int MaxDescriptionLength = 512;

    /// <summary>IP 地址最大长度（IPv4 字符串，最长 15 字符；预留 IPv6）</summary>
    public const int MaxIpAddressLength = 64;

    /// <summary>固件版本字符串最大长度</summary>
    public const int MaxFirmwareVersionLength = 64;

    /// <summary>操作日志消息最大长度</summary>
    public const int MaxLogMessageLength = 512;

    /// <summary>数据库表名前缀</summary>
    public const string DbTablePrefix = "AbpPro";
}

/// <summary>
/// 投影机当前连接状态
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
/// 投影机灯（LED）状态
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
/// 投影机操作日志类型
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
}
