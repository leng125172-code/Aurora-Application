using System.ComponentModel.DataAnnotations;
using AuroraStruct3D.Cameras;

namespace AuroraStruct3D.Cameras.Dtos;

/// <summary>
/// 创建相机设备请求DTO
/// </summary>
public class CreateCameraDeviceDto
{
    /// <summary>相机显示名称</summary>
    [Required]
    [MaxLength(CameraConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    /// <summary>设备物理索引（SDK中从0开始的编号）</summary>
    [Range(0, 63)]
    public int DeviceIndex { get; set; }

    /// <summary>描述</summary>
    [MaxLength(CameraConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// 更新相机设备请求DTO
/// </summary>
public class UpdateCameraDeviceDto
{
    /// <summary>相机显示名称</summary>
    [Required]
    [MaxLength(CameraConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    /// <summary>描述</summary>
    [MaxLength(CameraConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// 相机列表查询请求DTO
/// </summary>
public class GetCameraListDto : Volo.Abp.Application.Dtos.PagedAndSortedResultRequestDto
{
    /// <summary>名称关键字过滤</summary>
    public string? Filter { get; set; }
}

/// <summary>
/// 创建参数集请求DTO
/// </summary>
public class CreateCameraParameterSetDto
{
    /// <summary>所属相机设备ID</summary>
    [Required]
    public Guid CameraDeviceId { get; set; }

    /// <summary>参数集名称</summary>
    [Required]
    [MaxLength(CameraConsts.MaxParameterSetNameLength)]
    public string Name { get; set; } = null!;

    /// <summary>描述</summary>
    [MaxLength(CameraConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>是否为默认参数集</summary>
    public bool IsDefault { get; set; }

    /// <summary>排序序号</summary>
    public int SortOrder { get; set; }

    /// <summary>初始参数列表</summary>
    public List<CreateCameraParameterDto> Parameters { get; set; } = new();
}

/// <summary>
/// 更新参数集请求DTO
/// </summary>
public class UpdateCameraParameterSetDto
{
    /// <summary>参数集名称</summary>
    [Required]
    [MaxLength(CameraConsts.MaxParameterSetNameLength)]
    public string Name { get; set; } = null!;

    /// <summary>描述</summary>
    [MaxLength(CameraConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>是否为默认参数集</summary>
    public bool IsDefault { get; set; }

    /// <summary>排序序号</summary>
    public int SortOrder { get; set; }

    /// <summary>参数列表（全量替换）</summary>
    public List<CreateCameraParameterDto> Parameters { get; set; } = new();
}

/// <summary>
/// 创建/更新参数项请求DTO
/// </summary>
public class CreateCameraParameterDto
{
    /// <summary>参数键名</summary>
    [Required]
    [MaxLength(CameraConsts.MaxParameterKeyLength)]
    public string ParamKey { get; set; } = null!;

    /// <summary>参数类型</summary>
    public CameraParameterType ParamType { get; set; }

    /// <summary>参数值</summary>
    [Required]
    [MaxLength(CameraConsts.MaxParameterValueLength)]
    public string Value { get; set; } = null!;

    /// <summary>描述</summary>
    [MaxLength(CameraConsts.MaxDescriptionLength)]
    public string? Description { get; set; }
}

/// <summary>
/// 应用参数集到相机请求DTO
/// </summary>
public class ApplyCameraParameterSetDto
{
    /// <summary>参数集ID</summary>
    [Required]
    public Guid ParameterSetId { get; set; }
}

// ─── 手动控制：设备信息 ──────────────────────────────────────────────────────

/// <summary>
/// 相机硬件设备信息 DTO
/// </summary>
public class CameraDeviceInfoDto
{
    /// <summary>相机型号</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>序列号</summary>
    public string SerialNumber { get; set; } = string.Empty;

    /// <summary>固件版本</summary>
    public string FirmwareVersion { get; set; } = string.Empty;

    /// <summary>FPGA 版本</summary>
    public string FpgaVersion { get; set; } = string.Empty;

    /// <summary>FPGA 温度（摄氏度）</summary>
    public int FpgaTemperature { get; set; }

    /// <summary>传感器温度（摄氏度，来自 TUIDP_TEMPERATURE）</summary>
    public double SensorTemperature { get; set; }

    /// <summary>当前图像宽度（像素）</summary>
    public int CurrentWidth { get; set; }

    /// <summary>当前图像高度（像素）</summary>
    public int CurrentHeight { get; set; }
}

// ─── 手动控制：图像参数 ──────────────────────────────────────────────────────

/// <summary>
/// 相机图像采集参数 DTO（ROI、位深、翻转、Binning、图像处理参数等）
/// </summary>
public class CameraImageParamsDto
{
    /// <summary>ROI 使能</summary>
    public bool RoiEnabled { get; set; }

    /// <summary>ROI 水平偏移（像素）</summary>
    public int RoiHOffset { get; set; }

    /// <summary>ROI 垂直偏移（像素）</summary>
    public int RoiVOffset { get; set; }

    /// <summary>ROI 宽度</summary>
    public int RoiWidth { get; set; }

    /// <summary>ROI 高度</summary>
    public int RoiHeight { get; set; }

    /// <summary>像素位深度</summary>
    public CameraPixelDepth PixelDepth { get; set; }

    /// <summary>水平镜像</summary>
    public bool HorizontalFlip { get; set; }

    /// <summary>垂直镜像</summary>
    public bool VerticalFlip { get; set; }

    /// <summary>Binning 模式</summary>
    public CameraBinningMode Binning { get; set; }

    /// <summary>Gamma 使能</summary>
    public bool GammaEnabled { get; set; }

    /// <summary>Gamma 值</summary>
    public double Gamma { get; set; }

    /// <summary>对比度</summary>
    public double Contrast { get; set; }

    /// <summary>亮度</summary>
    public double Brightness { get; set; }

    /// <summary>目标帧率</summary>
    public double FrameRate { get; set; }

    /// <summary>当前最大帧率（只读）</summary>
    public double FrameRateMax { get; set; }
}

/// <summary>
/// 设置图像参数请求 DTO
/// </summary>
public class SetCameraImageParamsDto
{
    /// <summary>ROI 使能</summary>
    public bool? RoiEnabled { get; set; }

    /// <summary>ROI 水平偏移（像素）</summary>
    public int? RoiHOffset { get; set; }

    /// <summary>ROI 垂直偏移（像素）</summary>
    public int? RoiVOffset { get; set; }

    /// <summary>ROI 宽度</summary>
    public int? RoiWidth { get; set; }

    /// <summary>ROI 高度</summary>
    public int? RoiHeight { get; set; }

    /// <summary>像素位深度</summary>
    public CameraPixelDepth? PixelDepth { get; set; }

    /// <summary>水平镜像</summary>
    public bool? HorizontalFlip { get; set; }

    /// <summary>垂直镜像</summary>
    public bool? VerticalFlip { get; set; }

    /// <summary>Binning 模式</summary>
    public CameraBinningMode? Binning { get; set; }

    /// <summary>Gamma 使能</summary>
    public bool? GammaEnabled { get; set; }

    /// <summary>Gamma 值</summary>
    public double? Gamma { get; set; }

    /// <summary>对比度</summary>
    public double? Contrast { get; set; }

    /// <summary>亮度</summary>
    public double? Brightness { get; set; }

    /// <summary>目标帧率</summary>
    public double? FrameRate { get; set; }
}

// ─── 手动控制：采集参数 ──────────────────────────────────────────────────────

/// <summary>
/// 相机采集参数 DTO（AE、增益、曝光等）
/// </summary>
public class CameraAcquisitionParamsDto
{
    /// <summary>自动曝光模式</summary>
    public CameraAutoExposureMode AeMode { get; set; }

    /// <summary>当前 AE 状态（0=未运行 1=运行中，只读）</summary>
    public int AeStatus { get; set; }

    /// <summary>AE 目标灰度值</summary>
    public double AeTargetGray { get; set; }

    /// <summary>AE 最大曝光时间限制（μs）</summary>
    public double AeMaxExposure { get; set; }

    /// <summary>AE 最小曝光时间限制（μs）</summary>
    public double AeMinExposure { get; set; }

    /// <summary>增益模式（HDR/High/Low）</summary>
    public CameraGainMode GainMode { get; set; }

    /// <summary>曝光时间（μs）</summary>
    public double ExposureTime { get; set; }

    /// <summary>全局增益</summary>
    public double GlobalGain { get; set; }
}

/// <summary>
/// 设置采集参数请求 DTO
/// </summary>
public class SetCameraAcquisitionParamsDto
{
    /// <summary>自动曝光模式</summary>
    public CameraAutoExposureMode? AeMode { get; set; }

    /// <summary>AE 目标灰度值</summary>
    public double? AeTargetGray { get; set; }

    /// <summary>AE 最大曝光时间限制（μs）</summary>
    public double? AeMaxExposure { get; set; }

    /// <summary>AE 最小曝光时间限制（μs）</summary>
    public double? AeMinExposure { get; set; }

    /// <summary>增益模式</summary>
    public CameraGainMode? GainMode { get; set; }

    /// <summary>曝光时间（μs）</summary>
    public double? ExposureTime { get; set; }

    /// <summary>全局增益</summary>
    public double? GlobalGain { get; set; }
}

// ─── 手动控制：触发参数 ──────────────────────────────────────────────────────

/// <summary>
/// 触发输出端口参数 DTO
/// </summary>
public class CameraTriggerOutDto
{
    /// <summary>端口编号（0/1/2）</summary>
    public int Port { get; set; }

    /// <summary>输出模式/信号来源</summary>
    public int Mode { get; set; }

    /// <summary>边沿模式（0=上升沿 1=下降沿）</summary>
    public int EdgeMode { get; set; }

    /// <summary>延迟时间</summary>
    public int DelayTm { get; set; }

    /// <summary>脉冲宽度</summary>
    public int Width { get; set; }
}

/// <summary>
/// 相机触发参数 DTO
/// </summary>
public class CameraTriggerParamsDto
{
    /// <summary>触发模式（0=连续 1=标准 2=同步 3=全局 4=软件）</summary>
    public int TriggerMode { get; set; }

    /// <summary>曝光模式（0=全局 1=电子滚动）</summary>
    public int ExpMode { get; set; }

    /// <summary>触发边沿（0=上升 1=下降）</summary>
    public int EdgeMode { get; set; }

    /// <summary>延迟时间</summary>
    public int DelayTm { get; set; }

    /// <summary>单次触发帧数</summary>
    public int Frames { get; set; }

    /// <summary>缓冲帧数</summary>
    public int BufFrames { get; set; }

    /// <summary>触发输出端口 1 参数</summary>
    public CameraTriggerOutDto TriggerOut1 { get; set; } = new();

    /// <summary>触发输出端口 2 参数</summary>
    public CameraTriggerOutDto TriggerOut2 { get; set; } = new();

    /// <summary>触发输出端口 3 参数</summary>
    public CameraTriggerOutDto TriggerOut3 { get; set; } = new();
}

/// <summary>
/// 设置触发参数请求 DTO
/// </summary>
public class SetCameraTriggerParamsDto
{
    /// <summary>触发模式</summary>
    public int? TriggerMode { get; set; }

    /// <summary>曝光模式</summary>
    public int? ExpMode { get; set; }

    /// <summary>触发边沿</summary>
    public int? EdgeMode { get; set; }

    /// <summary>延迟时间</summary>
    public int? DelayTm { get; set; }

    /// <summary>单次触发帧数</summary>
    public int? Frames { get; set; }

    /// <summary>缓冲帧数</summary>
    public int? BufFrames { get; set; }

    /// <summary>触发输出端口 1 参数（null 表示不修改）</summary>
    public CameraTriggerOutDto? TriggerOut1 { get; set; }

    /// <summary>触发输出端口 2 参数</summary>
    public CameraTriggerOutDto? TriggerOut2 { get; set; }

    /// <summary>触发输出端口 3 参数</summary>
    public CameraTriggerOutDto? TriggerOut3 { get; set; }
}

// ─── 手动控制：自定义参数（颜色/WB/LED）─────────────────────────────────────

/// <summary>
/// 白平衡计算区域 DTO
/// </summary>
public class CameraCalcRoiDto
{
    /// <summary>计算区域使能</summary>
    public bool Enabled { get; set; }

    /// <summary>水平偏移（像素）</summary>
    public int HOffset { get; set; }

    /// <summary>垂直偏移（像素）</summary>
    public int VOffset { get; set; }

    /// <summary>宽度</summary>
    public int Width { get; set; }

    /// <summary>高度</summary>
    public int Height { get; set; }
}

/// <summary>
/// 相机自定义参数 DTO（白平衡通道增益、饱和度、色温、LED 等）
/// </summary>
public class CameraCustomParamsDto
{
    /// <summary>白平衡模式</summary>
    public CameraWhiteBalanceMode WbMode { get; set; }

    /// <summary>R 通道增益</summary>
    public double ChannelGainR { get; set; }

    /// <summary>G 通道增益</summary>
    public double ChannelGainG { get; set; }

    /// <summary>B 通道增益</summary>
    public double ChannelGainB { get; set; }

    /// <summary>饱和度</summary>
    public double Saturation { get; set; }

    /// <summary>色温</summary>
    public double ColorTemperature { get; set; }

    /// <summary>白平衡计算区域</summary>
    public CameraCalcRoiDto WbCalcRoi { get; set; } = new();

    /// <summary>LED 使能</summary>
    public bool LedEnabled { get; set; }

    /// <summary>当前缓冲帧数（近似触发计数器，只读）</summary>
    public int CurrentBufFrames { get; set; }
}

/// <summary>
/// 设置自定义参数请求 DTO
/// </summary>
public class SetCameraCustomParamsDto
{
    /// <summary>白平衡模式</summary>
    public CameraWhiteBalanceMode? WbMode { get; set; }

    /// <summary>R 通道增益</summary>
    public double? ChannelGainR { get; set; }

    /// <summary>G 通道增益</summary>
    public double? ChannelGainG { get; set; }

    /// <summary>B 通道增益</summary>
    public double? ChannelGainB { get; set; }

    /// <summary>饱和度</summary>
    public double? Saturation { get; set; }

    /// <summary>色温</summary>
    public double? ColorTemperature { get; set; }

    /// <summary>白平衡计算区域</summary>
    public CameraCalcRoiDto? WbCalcRoi { get; set; }

    /// <summary>LED 使能</summary>
    public bool? LedEnabled { get; set; }
}

// ─── 手动控制：实时指标 ──────────────────────────────────────────────────────

/// <summary>
/// 相机实时运行指标 DTO（通过 SignalR 周期推送）
/// </summary>
public class CameraLiveMetricsDto
{
    /// <summary>FPGA 温度（摄氏度）</summary>
    public int FpgaTemperature { get; set; }

    /// <summary>传感器温度（摄氏度）</summary>
    public double SensorTemperature { get; set; }

    /// <summary>当前实际帧率</summary>
    public double FrameRate { get; set; }

    /// <summary>AE 状态（0=未运行 1=运行中）</summary>
    public int AeStatus { get; set; }

    /// <summary>当前 USB 缓冲帧数（近似触发计数）</summary>
    public int CurrentBufFrames { get; set; }
}

// ─── 手动控制：预览与快照 ────────────────────────────────────────────────────

/// <summary>
/// 开启相机预览请求 DTO
/// </summary>
public class StartCameraPreviewDto
{
    /// <summary>SignalR 连接 ID（用于精准推送给请求方）</summary>
    public string? ConnectionId { get; set; }

    /// <summary>是否同时启动 RTP/MJPEG UDP 流</summary>
    public bool EnableRtp { get; set; } = true;
}

/// <summary>
/// RTP 推流端点信息 DTO
/// </summary>
public class CameraRtpEndpointDto
{
    /// <summary>RTP UDP 地址（格式 host:port）</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>VLC 可打开的 SDP 内容（Base64 编码）</summary>
    public string SdpBase64 { get; set; } = string.Empty;
}

/// <summary>
/// 单帧快照结果 DTO
/// </summary>
public class CameraSnapshotDto
{
    /// <summary>Base64 编码的 JPEG 数据 URI（data:image/jpeg;base64,...）</summary>
    public string DataUri { get; set; } = string.Empty;

    /// <summary>拍摄时间戳（UTC）</summary>
    public DateTime CapturedAt { get; set; }
}

// ─── 手动控制：用户配置文件 ──────────────────────────────────────────────────

/// <summary>
/// 加载或保存用户配置文件请求 DTO
/// </summary>
public class CameraUserProfileDto
{
    /// <summary>配置文件名称（对应 SDK profileName 参数）</summary>
    public string ProfileName { get; set; } = string.Empty;
}
