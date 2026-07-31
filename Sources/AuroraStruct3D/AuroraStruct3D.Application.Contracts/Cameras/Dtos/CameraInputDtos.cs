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
    public int? FpgaTemperature { get; set; }

    /// <summary>传感器温度（摄氏度，来自 TUIDP_TEMPERATURE）</summary>
    public double? SensorTemperature { get; set; }

    /// <summary>当前图像宽度（像素）</summary>
    public int CurrentWidth { get; set; }

    /// <summary>当前图像高度（像素）</summary>
    public int CurrentHeight { get; set; }
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

    /// <summary>对焦清晰度评分（0-100，越高越清晰；仅预览时有效）</summary>
    public float FocusScore { get; set; }

    /// <summary>曝光质量评分（0-100，越高曝光越适中；仅预览时有效）</summary>
    public float ApertureScore { get; set; }

    /// <summary>光圈调节建议（0=良好, 1=缩小光圈, 2=增大光圈）</summary>
    public int ApertureHint { get; set; }
}

// ─── 手动控制：预览与快照 ────────────────────────────────────────────────────

/// <summary>
/// 开启相机预览请求 DTO
/// </summary>
public class StartCameraPreviewDto
{
    /// <summary>SignalR 连接 ID（用于状态/指标精准推送和宽限期判断，不再用于按帧推送）</summary>
    public string? ConnectionId { get; set; }

    /// <summary>是否同时启动 RTP/MJPEG UDP 流</summary>
    public bool EnableRtp { get; set; } = false;
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

// ─── 通用 GenICam 节点读写 ────────────────────────────────────────────────────

/// <summary>
/// 读取单个 GenICam 节点请求 DTO
/// </summary>
public class GenICamNodeGetInput
{
    /// <summary>GenICam 节点名称（区分大小写）</summary>
    public string NodeName { get; set; } = string.Empty;

    /// <summary>数据类型："int" / "float" / "string"（enum 和 bool 也用 "int" 表示）</summary>
    public string DataType { get; set; } = "int";
}

/// <summary>
/// 批量读取 GenICam 节点请求 DTO
/// </summary>
public class GenICamBatchGetInput
{
    /// <summary>要读取的节点列表</summary>
    public List<GenICamNodeGetInput> Nodes { get; set; } = new();
}

/// <summary>
/// 单个 GenICam 节点读取结果 DTO
/// </summary>
public class GenICamNodeResultDto
{
    /// <summary>节点名称</summary>
    public string NodeName { get; set; } = string.Empty;

    /// <summary>节点值（统一转为字符串；浮点数使用 InvariantCulture 序列化）</summary>
    public string? Value { get; set; }

    /// <summary>读取是否成功</summary>
    public bool Success { get; set; }

    /// <summary>失败时的错误信息</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>节点当前访问模式（"ReadOnly" / "ReadWrite" / "WriteOnly" 等）；读取失败时为 null</summary>
    public string? Access { get; set; }
}

/// <summary>
/// 批量读取 GenICam 节点结果 DTO
/// </summary>
public class GenICamBatchGetResultDto
{
    /// <summary>各节点读取结果</summary>
    public List<GenICamNodeResultDto> Results { get; set; } = new();
}

/// <summary>
/// 写入 GenICam 节点请求 DTO。
/// 兼容两种 JSON 格式：
/// <list type="bullet">
/// <item>单节点：<c>{"nodeName":"X","dataType":"float","value":"45.0"}</c></item>
/// <item>批量：<c>{"nodes":[{"nodeName":"X","value":"45.0"},{...}]}</c></item>
/// </list>
/// 当 <see cref="Nodes"/> 非 null 时进入批量模式，节点类型由后端从缓存 NodeMap 自动推断。
/// </summary>
public class GenICamNodeSetInput
{
    // ─── 单节点模式 ───────────────────────────────────────────────────────────

    /// <summary>GenICam 节点名称（单节点模式）</summary>
    public string? NodeName { get; set; }

    /// <summary>数据类型："int" / "float" / "string"（enum 和 bool 也用 "int" 表示；单节点模式）</summary>
    public string DataType { get; set; } = "int";

    /// <summary>要写入的值，字符串序列化（单节点模式）</summary>
    public string? Value { get; set; }

    // ─── 批量模式 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 批量节点列表（批量模式）。非 null 时忽略 NodeName / DataType / Value 字段。
    /// 每个条目只需 nodeName + value，类型由后端从缓存 NodeMap 自动推断。
    /// </summary>
    public List<GenICamBatchSetItem>? Nodes { get; set; }
}

/// <summary>
/// 批量写入 GenICam 节点的单个条目（无需指定类型）
/// </summary>
public class GenICamBatchSetItem
{
    /// <summary>GenICam 节点名称</summary>
    public string NodeName { get; set; } = string.Empty;

    /// <summary>要写入的值（字符串序列化）</summary>
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// GenICam 节点增量变更推送 DTO（SignalR OnGenICamNodesChangedAsync 使用）
/// </summary>
public class GenICamNodeChangeDto
{
    /// <summary>节点名称</summary>
    public string NodeName { get; set; } = string.Empty;

    /// <summary>当前值（统一字符串表示，读取失败时为 null）</summary>
    public string? Value { get; set; }

    /// <summary>当前访问模式（"ReadOnly" / "ReadWrite" / "WriteOnly" / "NotAvailable" 等）</summary>
    public string? Access { get; set; }

    /// <summary>是否被锁定</summary>
    public bool IsLocked { get; set; }
}

/// <summary>
/// 查询相机操作日志请求DTO
/// </summary>
public class GetCameraLogListDto : Volo.Abp.Application.Dtos.PagedResultRequestDto
{
    /// <summary>相机设备ID（必填，无 ID 则返回空结果）</summary>
    public Guid? CameraDeviceId { get; set; }

    /// <summary>按操作类型过滤（可选）</summary>
    public CameraOperationType? OperationType { get; set; }

    /// <summary>仅返回失败记录</summary>
    public bool? IsFailedOnly { get; set; }

    /// <summary>开始时间（可选，UTC）</summary>
    public DateTime? StartTime { get; set; }

    /// <summary>结束时间（可选，UTC）</summary>
    public DateTime? EndTime { get; set; }
}

// ─── 手动控制：图像旋转角度 ─────────────────────────────────────────────

/// <summary>
/// 设置相机软件端图像旋转角度（仅支持 0/90/180/270）
/// </summary>
public class SetCameraRotationAngleDto
{
    /// <summary>旋转角度，允许值：0、90、180、270</summary>
    public int Angle { get; set; }
}
