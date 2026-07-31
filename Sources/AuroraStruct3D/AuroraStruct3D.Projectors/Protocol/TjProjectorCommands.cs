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

    /// <summary>保存条纹参数到内部 Flash 命令（Ms = Make Save fringe）</summary>
    public const string SaveFringeParams = "Ms";

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
    /// 设置图像重复参数命令前缀（20幅图、每幅显示一次的完整命令为 "MA 0 19 0 0"）。
    /// 参数1：每幅图片的额外重复次数（0 表示只显示一次）；
    /// 参数2：末帧索引，图片从0开始编号，因此必须传入实际幅数减1；
    /// 参数3：固定为0；参数4：固定为0。
    /// </summary>
    public const string SetImageRepeatPrefix = "MA ";

    /// <summary>
    /// 设置下载时分配的总图像幅数（完整命令如 "MB 4"）。
    /// 下载阶段直接传入实际条纹幅数；不要套用 MA 参数2的末帧索引语义。
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
    /// 参数表示序列开头连续的横条纹幅数，后续图像全部为竖条纹；0 表示全部为竖条纹。
    /// </summary>
    public const string SetFringeDirectionPrefix = "MD ";

    /// <summary>
    /// 配置每幅条纹的横竖方向位图命令前缀（完整命令如 "MF 0 85 85 85 85"）。
    /// 第一个参数块索引范围 0~3（MF 与块索引之间有空格），每块覆盖 32 幅图；后四个参数为 4 个字节位图。
    /// 每幅图占 1bit：0=竖条纹，1=横条纹。
    /// 字节内采用 LSB first（低位在前）：每块中第1幅图→byte0的bit0，第8幅图→byte0的bit7，第9幅→byte1的bit0...
    /// 1-2-1-2 横竖交替模式（横为1）每个字节为 0x55（01010101b=85）。
    /// </summary>
    public const string SetFringeOrientationBitmapPrefix = "MF ";

    /// <summary>
    /// 写 Flash 像素列命令前缀（完整命令如 "FW128 200"）。
    /// 第一个参数：当前像素列全局索引；第二个参数：该列灰度值（0~255）。
    /// 参考官方 TJEasy USB Demo，发送后无需等待应答。
    /// </summary>
    public const string WriteFlashPixelPrefix = "FW";

    /// <summary>
    /// 写 Flash 像素列命令前缀（一次写入8个像素，完整命令如 "FF0 255 0 255 0 255 0 255"）。
    /// 参数1：当前像素列全局索引（从0开始）；参数2~9：8个像素列的灰度值（0~255）。
    /// 每写入 32 个数据组（32×8=256像素列）后需等待光机 page 写入完成的应答。
    /// </summary>
    public const string WriteFlashPixelBatchPrefix = "FF";
}

/// <summary>
/// 投影仪内容显示模式
/// </summary>
// ProjectorDisplayMode 和 ProjectorColor 已移至 AuroraStruct3D.Domain.Shared/Projectors/ProjectorConsts.cs
