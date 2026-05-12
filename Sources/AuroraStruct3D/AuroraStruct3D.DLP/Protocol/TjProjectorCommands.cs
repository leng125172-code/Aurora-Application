namespace AuroraStruct3D.DLP;

/// <summary>
/// 腾聚（TJ）结构光投影机 ASCII 协议命令字常量。
/// 协议基于 TCP 端口 1234，所有命令均为 ASCII 文本，以 \r\n 结尾。
/// 命令字从 TJProjector.cpp / TJSTProjectorApi.cpp 逆向提取。
/// </summary>
internal static class TjProjectorCommands
{
    // ─── LED 控制 ──────────────────────────────────────────────────
    /// <summary>开灯（LedOn）</summary>
    public const string LedOn = "LN\r\n";

    /// <summary>关灯（LedOff）</summary>
    public const string LedOff = "LL\r\n";

    // ─── 亮度控制 ──────────────────────────────────────────────────
    /// <summary>
    /// 高亮模式使能（v>175时先发送此帧再设置亮度）
    /// </summary>
    public const string HighLightEnable = "LZ 1\r\n";

    /// <summary>
    /// 设置亮度命令前缀，实际命令形如 "LA 100\r\n"（范围 10~200）
    /// </summary>
    public const string SetLightPrefix = "LA ";

    // ─── 颜色控制（仅多光谱结构光投影机支持） ────────────────────
    /// <summary>红色</summary>
    public const string ColorRed = "LR\r\n";

    /// <summary>绿色</summary>
    public const string ColorGreen = "LG\r\n";

    /// <summary>蓝色</summary>
    public const string ColorBlue = "LB\r\n";

    /// <summary>白色（全色）</summary>
    public const string ColorWhite = "LC\r\n";

    // ─── 内容模式控制 ──────────────────────────────────────────────
    /// <summary>
    /// 设置内容模式命令前缀字节（ASCII '0'+nMode，例如 mode=0 → 'S','0','\r','\n'）
    /// nMode：0=黑屏，1=白屏，2=十字，3=棋盘
    /// </summary>
    public const byte SetModePrefixByte1 = (byte)'S';

    public const byte SetModeOffsetBase = (byte)'0';

    // ─── 触发条纹投影 ──────────────────────────────────────────────
    /// <summary>触发一次（条纹末尾为白色，nGray=255 时使用此命令）</summary>
    public const string TriggerOnce = "T\r\n";

    /// <summary>触发一次并指定末尾灰度（命令前缀，完整命令如 "G 128\r\n"）</summary>
    public const string TriggerWithGrayPrefix = "G ";

    // ─── 设备信息查询 ──────────────────────────────────────────────
    /// <summary>读取固件版本号</summary>
    public const string ReadVersion = "v\r\n";

    /// <summary>读取设备标志字节（读取设备ID）</summary>
    public const string ReadFlagByte = "pr 0\r\n";

    /// <summary>TCP 服务端口（固定 1234）</summary>
    public const int TcpPort = 1234;

    /// <summary>指定末尾灰度的触发命令结尾</summary>
    public const string CommandSuffix = "\r\n";
}

/// <summary>
/// 投影机内容显示模式
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
}

/// <summary>
/// 投影机颜色（仅多光谱结构光投影机支持）
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
}
