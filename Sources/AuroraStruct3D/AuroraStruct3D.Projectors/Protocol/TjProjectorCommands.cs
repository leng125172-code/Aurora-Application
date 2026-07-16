namespace AuroraStruct3D.Projectors;

/// <summary>
/// 腾聚（TJ）结构光投影仪 ASCII 协议命令字常量。
/// 协议基于 TCP 端口 1234，所有命令均为 ASCII 文本，以  结尾。
/// 命令字从 TJProjector.cpp / TJSTProjectorApi.cpp 逆向提取。
/// </summary>
internal static class TjProjectorCommands
{
    // ─── LED 控制 ──────────────────────────────────────────────────
    /// <summary>开灯（LedOn）</summary>
    public const string LedOn = "LN";

    /// <summary>关灯（LedOff）</summary>
    public const string LedOff = "LL";

    // ─── 亮度控制 ──────────────────────────────────────────────────
    /// <summary>
    /// 高亮模式使能（v>175时先发送此帧再设置亮度）
    /// </summary>
    public const string HighLightEnable = "LZ 1";

    /// <summary>
    /// 设置亮度命令前缀，实际命令形如 "LA 100"（范围 10~200）
    /// </summary>
    public const string SetLightPrefix = "LA ";

    // ─── 颜色控制（仅多光谱结构光投影仪支持） ────────────────────
    /// <summary>红色</summary>
    public const string ColorRed = "LR";

    /// <summary>绿色</summary>
    public const string ColorGreen = "LG";

    /// <summary>蓝色</summary>
    public const string ColorBlue = "LB";

    /// <summary>白色（全色）</summary>
    public const string ColorWhite = "LC";

    // ─── 内容模式控制 ──────────────────────────────────────────────
    /// <summary>
    /// 设置内容模式命令前缀字节（ASCII '0'+nMode，例如 mode=0 → 'S','0','\r','\n'）
    /// nMode：0=黑屏，1=白屏，2=十字，3=棋盘
    /// </summary>
    public const byte SetModePrefixByte1 = (byte)'S';

    public const byte SetModeOffsetBase = (byte)'0';

    // ─── 触发条纹投影 ──────────────────────────────────────────────
    /// <summary>触发一次（条纹末尾为白色，nGray=255 时使用此命令）</summary>
    public const string TriggerOnce = "T";

    /// <summary>单帧触发模式下切换到下一张条纹（B 2 + N）。</summary>
    public const string TriggerNextFrame = "N";

    /// <summary>触发一次并指定末尾灰度（命令前缀，完整命令如 "G 128"）</summary>
    public const string TriggerWithGrayPrefix = "G ";

    // ─── 设备信息查询 ──────────────────────────────────────────────
    /// <summary>读取固件版本号</summary>
    public const string ReadVersion = "v";

    /// <summary>读取设备标志字节（读取设备ID）</summary>
    public const string ReadFlagByte = "pr 0";

    /// <summary>TCP 服务端口（固定 1234）</summary>
    public const int TcpPort = 1234;

    // ─── 高级控制命令（Phase 4 新增）──────────────────────────────

    /// <summary>设置图像翻转命令前缀（完整命令如 "S9 0"，参数为 ProjectorFlipMode 的整数值）</summary>
    public const string SetFlipPrefix = "S9 ";

    /// <summary>设置触发模式命令前缀（完整命令如 "B 0"，参数为 ProjectorTriggerMode 的整数值）</summary>
    public const string SetTriggerModePrefix = "B ";

    /// <summary>设置开机图案命令前缀（完整命令如 "S8 2"，参数为 ProjectorBootImage 的整数值）</summary>
    public const string SetBootImagePrefix = "S8 ";

    /// <summary>设置 RGB 彩光分量亮度命令前缀（完整命令如 "LE 92 94 94"，一条命令同时使能并设置 R G B）</summary>
    public const string SetRgbColorPrefix = "LE ";

    /// <summary>设置棋盘格像素尺寸命令前缀（完整命令如 "S11 30"）</summary>
    public const string SetCheckerboardPixelPrefix = "S11 ";

    /// <summary>软复位命令（重启投影仪固件）</summary>
    public const string SoftReset = "X";

    /// <summary>保存参数到内部 Flash 命令（MS = Make Save）</summary>
    public const string SaveParams = "MS";

    /// <summary>读寄存器命令前缀（完整命令如 "pr 3"）</summary>
    public const string ReadRegisterPrefix = "pr ";

    /// <summary>写寄存器命令前缀（完整命令如 "pw 3 1"）</summary>
    public const string WriteRegisterPrefix = "pw ";

    // ─── 条纹 Flash 下载相关命令 ─────────────────────────────────

    /// <summary>
    /// 查询投影仪像素分辨率（Fp 指令），响应格式示例："1280 Pixel Mode"
    /// </summary>
    public const string ReadPixelMode = "Fp";

    /// <summary>
    /// 设置总图像幅数命令前缀（完整命令如 "MB 3"）
    /// </summary>
    public const string SetImageCountPrefix = "MB ";

    /// <summary>
    /// 擦除 Flash 命令。等待回复："F0" 表示成功，"F1" 表示失败需重试
    /// </summary>
    public const string EraseFlash = "FE";

    /// <summary>Flash 擦除成功回复标识</summary>
    public const string FlashEraseOk = "F0";

    /// <summary>Flash 擦除失败回复标识（需重试）</summary>
    public const string FlashEraseFail = "F1";

    /// <summary>
    /// 设置条纹方向命令前缀（完整命令如 "MD 3" 或 "MD 0"）。
    /// 横条纹时传入图像幅数；竖条纹时传入 0。
    /// </summary>
    public const string SetFringeDirectionPrefix = "MD ";

    /// <summary>
    /// 配置每幅条纹的横竖方向位图（完整命令如 "MF0 41 0 0 0"）。
    /// 第一个参数块索引范围 0~3，每块覆盖 32 幅图；后四个参数为 4 个字节位图。
    /// 每幅图占 1bit：0=竖条纹，1=横条纹。
    /// </summary>
    public const string SetFringeOrientationBitmapPrefix = "MF";

    /// <summary>
    /// 写 Flash 像素列命令前缀（完整命令如 "FW 128 200"）。
    /// 第一个参数：当前像素列全局索引；第二个参数：该列灰度值（0~255）。
    /// 每写入 256 个数据后需等待光机 page 写入完成的应答，再继续写入。
    /// </summary>
    public const string WriteFlashPixelPrefix = "FW";
}

/// <summary>
/// 投影仪内容显示模式
/// </summary>
// ProjectorDisplayMode 和 ProjectorColor 已移至 AuroraStruct3D.Domain.Shared/Projectors/ProjectorConsts.cs
