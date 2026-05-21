namespace AuroraStruct3D.Tucam.Interop;

/// <summary>
/// TUCam API 返回值枚举，对应SDK中的 TUCAMRET
/// </summary>
public enum TUCamRet : uint
{
    // 成功状态
    /// <summary>无错误，通用成功码</summary>
    Success = 0x00000001,

    /// <summary>通用错误</summary>
    Failure = 0x80000000,

    // 初始化错误
    NoMemory = 0x80000101,
    NoResource = 0x80000102,
    NoModule = 0x80000103,
    NoDriver = 0x80000104,
    NoCamera = 0x80000105,
    NoGrabber = 0x80000106,
    NoProperty = 0x80000107,

    FailOpenCamera = 0x80000110,
    FailOpenBulkIn = 0x80000111,
    FailOpenBulkOut = 0x80000112,
    FailOpenControl = 0x80000113,
    FailCloseCamera = 0x80000114,
    FailOpenFile = 0x80000115,
    FailOpenCodec = 0x80000116,
    FailOpenContext = 0x80000117,

    // 状态错误
    Init = 0x80000201,
    Busy = 0x80000202,
    NotInit = 0x80000203,
    Excluded = 0x80000204,
    NotBusy = 0x80000205,
    NotReady = 0x80000206,

    // 等待错误
    Abort = 0x80000207,
    Timeout = 0x80000208,
    LostFrame = 0x80000209,
    MissFrame = 0x8000020A,
    UsbStatusError = 0x8000020B,
    FrameBufferFull = 0x8000020C,

    // 调用错误
    InvalidCamera = 0x80000301,
    InvalidHandle = 0x80000302,
    InvalidOption = 0x80000303,
    InvalidIdProp = 0x80000304,
    InvalidIdCapa = 0x80000305,
    InvalidIdParam = 0x80000306,
    InvalidParam = 0x80000307,
    InvalidFrameIdx = 0x80000308,
    InvalidValue = 0x80000309,
    InvalidEqual = 0x8000030A,
    InvalidChannel = 0x8000030B,
    InvalidSubArray = 0x8000030C,
    InvalidView = 0x8000030D,
    InvalidPath = 0x8000030E,
    InvalidIdVProp = 0x8000030F,

    NoValueText = 0x80000310,
    OutOfRange = 0x80000311,
    NotSupport = 0x80000312,
    NotWritable = 0x80000313,
    NotReadable = 0x80000314,

    WrongHandshake = 0x80000410,
    NewApiRequired = 0x80000411,
    AccessDeny = 0x80000412,

    NoCorrectData = 0x80000501,
    InvalidPrfSets = 0x80000601,
    InvalidIdPProp = 0x80000602,

    DecodeFailure = 0x80000701,
    CopyDataFailure = 0x80000702,
    EncodeFailure = 0x80000703,
    WriteFailure = 0x80000704,

    FailReadCamera = 0x83001001,
    FailWriteCamera = 0x83001002,
    OpticsUnplugged = 0x83001003,

    ReceiveFinish = 0x00000002,
    ExternalTrigger = 0x00000003,
}

/// <summary>
/// 相机能力ID枚举（对应SDK中的 TUCAM_IDCAPA）
/// </summary>
public enum TUCamIdCapa : int
{
    /// <summary>分辨率</summary>
    Resolution = 0x00,

    /// <summary>像素时钟</summary>
    PixelClock = 0x01,

    /// <summary>位深度</summary>
    BitOfDepth = 0x02,

    /// <summary>自动曝光</summary>
    AutoExposure = 0x03,

    /// <summary>水平翻转</summary>
    Horizontal = 0x04,

    /// <summary>垂直翻转</summary>
    Vertical = 0x05,

    /// <summary>自动白平衡</summary>
    AutoWhiteBalance = 0x06,

    /// <summary>风扇档位</summary>
    FanGear = 0x07,

    /// <summary>自动色阶</summary>
    AutoLevels = 0x08,

    /// <summary>直方图统计</summary>
    HistC = 0x0A,

    /// <summary>通道选择（彩色相机）</summary>
    Channels = 0x0B,

    /// <summary>增强</summary>
    Enhance = 0x0C,

    /// <summary>缺陷校正</summary>
    DftCorrection = 0x0D,

    /// <summary>启用降噪</summary>
    EnableDenoise = 0x0E,

    /// <summary>平场校正</summary>
    FltCorrection = 0x0F,

    /// <summary>数据格式（YUV/RAW）</summary>
    DataFormat = 0x11,

    /// <summary>垂直校正</summary>
    VerCorrection = 0x13,

    /// <summary>单色</summary>
    Monochrome = 0x14,

    /// <summary>黑平衡</summary>
    BlackBalance = 0x15,

    /// <summary>图像模式选择（CMS）</summary>
    ImgModeSelect = 0x16,

    /// <summary>HDR使能</summary>
    Hdr = 0x1C,

    /// <summary>图像处理使能</summary>
    EnableImgPro = 0x1D,

    /// <summary>时间戳使能</summary>
    EnableTimestamp = 0x1F,

    /// <summary>黑电平偏移使能</summary>
    EnableBlackLevel = 0x20,

    /// <summary>自动对焦</summary>
    AutoFocus = 0x21,

    /// <summary>自动曝光模式</summary>
    AutoExposureMode = 0x24,

    /// <summary>累加Binning</summary>
    BinningSum = 0x25,

    /// <summary>平均Binning</summary>
    BinningAvg = 0x26,

    /// <summary>TEC制冷使能</summary>
    EnableTec = 0x3B,

    /// <summary>Gamma使能</summary>
    EnableGamma = 0x3E,

    /// <summary>ISP使能</summary>
    EnableIsp = 0x43,

    /// <summary>快门模式</summary>
    Shutter = 0x46,
}

/// <summary>
/// 相机属性ID枚举（对应SDK中的 TUCAM_IDPROP）
/// </summary>
public enum TUCamIdProp : int
{
    /// <summary>全局增益</summary>
    GlobalGain = 0x00,

    /// <summary>曝光时间（微秒）</summary>
    ExposureTime = 0x01,

    /// <summary>亮度</summary>
    Brightness = 0x02,

    /// <summary>黑电平</summary>
    BlackLevel = 0x03,

    /// <summary>温度控制</summary>
    Temperature = 0x04,

    /// <summary>锐度</summary>
    Sharpness = 0x05,

    /// <summary>噪声等级</summary>
    NoiseLevel = 0x06,

    /// <summary>HDR K值</summary>
    HdrKValue = 0x07,

    /// <summary>Gamma</summary>
    Gamma = 0x08,

    /// <summary>对比度</summary>
    Contrast = 0x09,

    /// <summary>左色阶</summary>
    LeftLevels = 0x0A,

    /// <summary>右色阶</summary>
    RightLevels = 0x0B,

    /// <summary>通道增益</summary>
    ChannelGain = 0x0C,

    /// <summary>饱和度</summary>
    Saturation = 0x0D,

    /// <summary>色温</summary>
    ColorTemperature = 0x0E,

    /// <summary>色彩矩阵</summary>
    ColorMatrix = 0x0F,

    /// <summary>帧率</summary>
    FrameRate = 0x19,

    /// <summary>AE目标灰度</summary>
    AverageGray = 0x21,

    /// <summary>AE目标灰度阈值</summary>
    AverageGrayThreshold = 0x22,

    /// <summary>AE最大曝光时间限制</summary>
    ExposureMax = 0x25,

    /// <summary>AE最小曝光时间限制</summary>
    ExposureMin = 0x26,

    /// <summary>AE最大增益限制</summary>
    GainMax = 0x27,

    /// <summary>AE最小增益限制</summary>
    GainMin = 0x28,

    /// <summary>自动色阶忽略百分比（AE测光比例）</summary>
    AutoLevelPercentage = 0x2A,

    /// <summary>温度目标值</summary>
    TemperatureTarget = 0x2B,
}

/// <summary>
/// 相机信息ID枚举（对应SDK中的 TUCAM_IDINFO）
/// </summary>
public enum TUCamIdInfo : int
{
    /// <summary>总线类型（USB2.0/USB3.0）</summary>
    Bus = 0x01,

    /// <summary>供应商ID</summary>
    Vendor = 0x02,

    /// <summary>产品ID</summary>
    Product = 0x03,

    /// <summary>API版本</summary>
    ApiVersion = 0x04,

    /// <summary>固件版本</summary>
    FirmwareVersion = 0x05,

    /// <summary>FPGA版本</summary>
    FpgaVersion = 0x06,

    /// <summary>驱动版本</summary>
    DriverVersion = 0x07,

    /// <summary>传输速率</summary>
    TransferRate = 0x08,

    /// <summary>相机型号（字符串）</summary>
    CameraModel = 0x09,

    /// <summary>当前图像宽度</summary>
    CurrentWidth = 0x0A,

    /// <summary>当前图像高度</summary>
    CurrentHeight = 0x0B,

    /// <summary>图像通道数</summary>
    CameraChannels = 0x0C,

    /// <summary>是否已连接</summary>
    ConnectStatus = 0x18,

    /// <summary>USB总缓冲帧数</summary>
    TotalBufFrames = 0x19,

    /// <summary>USB当前缓冲帧数（近似展示触发计数器）</summary>
    CurrentBufFrames = 0x1A,

    /// <summary>FPGA温度</summary>
    FpgaTemperature = 0x13,

    /// <summary>PCBA温度</summary>
    PcbaTemperature = 0x14,
}

/// <summary>
/// 触发输出端口枚举（对应SDK TUCAM_OUTPUTTRG_PORT）
/// </summary>
public enum TUCamOutputTrgPort : int
{
    /// <summary>输出口一</summary>
    Port1 = 0x00,

    /// <summary>输出口二</summary>
    Port2 = 0x01,

    /// <summary>输出口三</summary>
    Port3 = 0x02,
}

/// <summary>
/// 触发输出模式（信号来源）枚举（对应SDK TUCAM_OUTPUTTRG_KIND）
/// </summary>
public enum TUCamOutputTrgKind : int
{
    /// <summary>低电平</summary>
    Low = 0x00,

    /// <summary>高电平</summary>
    High = 0x01,

    /// <summary>触发输入直通</summary>
    TriggerIn = 0x02,

    /// <summary>曝光开始</summary>
    ExposureStart = 0x03,

    /// <summary>全局曝光</summary>
    ExposureGlobal = 0x04,

    /// <summary>读取结束</summary>
    ReadEnd = 0x05,

    /// <summary>触发就绪</summary>
    TriggerReady = 0x06,
}

/// <summary>
/// 触发输出边沿枚举（对应SDK TUCAM_OUTPUTTRG_EDGE）
/// </summary>
public enum TUCamOutputTrgEdge : int
{
    /// <summary>上升沿</summary>
    Rising = 0x00,

    /// <summary>下降沿</summary>
    Falling = 0x01,
}

/// <summary>
/// 计算ROI类型枚举（白平衡/自动曝光测光区域）（对应SDK TUCAM_IDCROI）
/// </summary>
public enum TUCamIdCalcRoi : int
{
    /// <summary>白平衡计算区域</summary>
    WhiteBalance = 0x00,

    /// <summary>黑平衡计算区域</summary>
    BlackBalance = 0x01,

    /// <summary>黑电平偏移计算区域</summary>
    BlackLevelOffset = 0x02,

    /// <summary>自动对焦计算区域</summary>
    Focus = 0x03,

    /// <summary>自动曝光测光计算区域</summary>
    ExposureTime = 0x04,
}

/// <summary>
/// 供应商属性ID枚举（对应SDK TUCAM_IDVPROP）
/// </summary>
public enum TUCamIdVProp : int
{
    /// <summary>Flash地址</summary>
    AddrFlash = 0x00,

    /// <summary>HDR高增B偏移</summary>
    HdrHgBOffset = 0x03,

    /// <summary>HDR低增B偏移</summary>
    HdrLgBOffset = 0x04,

    /// <summary>FPN使能</summary>
    FpnEnable = 0x07,

    /// <summary>工作时间</summary>
    WorkingTime = 0x08,

    /// <summary>HDR低值</summary>
    HdrLValue = 0x0E,

    /// <summary>HDR高值</summary>
    HdrHValue = 0x0F,

    /// <summary>最大帧率</summary>
    MaxFrameRate = 0x1B,
}

/// <summary>
/// 采集模式枚举（对应SDK中的 TUCAM_CAPTURE_MODES）
/// </summary>
public enum TUCamCaptureMode : uint
{
    /// <summary>连续采集模式</summary>
    Sequence = 0x00,

    /// <summary>标准触发模式</summary>
    TriggerStandard = 0x01,

    /// <summary>同步触发模式</summary>
    TriggerSynchronous = 0x02,

    /// <summary>全局触发模式</summary>
    TriggerGlobal = 0x03,

    /// <summary>软件触发模式</summary>
    TriggerSoftware = 0x04,
}

/// <summary>
/// 图像格式枚举（对应SDK中的 TUIMG_FORMATS）
/// </summary>
public enum TUImgFormat : int
{
    Raw = 0x01,
    Tif = 0x02,
    Png = 0x04,
    Jpg = 0x08,
    Bmp = 0x10,
}
