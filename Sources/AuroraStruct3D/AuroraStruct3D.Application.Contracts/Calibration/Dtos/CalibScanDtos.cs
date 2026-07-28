using System.ComponentModel.DataAnnotations;

namespace AuroraStruct3D.Calibration.Dtos;

/// <summary>
/// 启动 Step6 在线扫描输入
/// </summary>
public class StartCalibScanInput
{
    /// <summary>标定项目 ID</summary>
    [Required]
    public Guid CalibProjectId { get; set; }

    /// <summary>
    /// 是否屏蔽投影仪控制。启用时仅打开投影仪灯光，不切换显示/触发模式，
    /// 不发送 T/N 指令，扫描按普通双目采集运行。
    /// </summary>
    public bool SuppressProjectorControl { get; set; }
}

/// <summary>
/// 停止 Step6 在线扫描输入
/// </summary>
public class StopCalibScanInput
{
    /// <summary>标定项目 ID</summary>
    [Required]
    public Guid CalibProjectId { get; set; }
}

/// <summary>
/// Step6 实时指标 DTO
/// </summary>
public class CalibScanMetricsDto
{
    /// <summary>当前预览帧率（FPS）</summary>
    public double Fps { get; set; }

    /// <summary>深度有效率（0~1，结构光扫描流程不再计算，固定为 0）</summary>
    public double DepthValidRate { get; set; }

    /// <summary>重建置信度（0~1，结构光扫描流程不再计算，固定为 0）</summary>
    public double Confidence { get; set; }

    /// <summary>深度图 JPEG DataUri（结构光扫描流程不再生成，固定为 null）</summary>
    public string? DepthMapDataUri { get; set; }

    /// <summary>当前帧序号</summary>
    public long FrameIndex { get; set; }

    /// <summary>指标时间戳（UTC）</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>当前轮次序号（从 1 开始递增）</summary>
    public long RoundIndex { get; set; }

    /// <summary>当前轮内帧序号（0~PatternCount-1，PatternCount 为每轮总帧数）</summary>
    public int FrameIndexInRound { get; set; }

    /// <summary>每轮总帧数（等于 Step3 CalibProjectorParam.PatternCount 的 2 倍，因横竖交替）</summary>
    public int PatternCount { get; set; }

    /// <summary>十字图检测结果（混合模式确认用，true 表示检测到十字图）</summary>
    public bool IsCrosshairDetected { get; set; }
}

/// <summary>
/// Step6 扫描相机角色枚举
/// </summary>
public enum CalibScanCameraRole
{
    /// <summary>主相机</summary>
    Main = 0,

    /// <summary>从相机</summary>
    Secondary = 1,
}

/// <summary>
/// Step6 扫描单帧图像推送 DTO（用于 SignalR 实时推送主/从相机原图）
/// </summary>
public class CalibScanFrameDto
{
    /// <summary>标定项目 ID</summary>
    public Guid CalibProjectId { get; set; }

    /// <summary>相机角色（0=主相机, 1=从相机）</summary>
    public CalibScanCameraRole CameraRole { get; set; }

    /// <summary>JPEG 原图二进制数据</summary>
    public byte[] JpegBytes { get; set; } = Array.Empty<byte>();

    /// <summary>当前轮次序号</summary>
    public long RoundIndex { get; set; }

    /// <summary>当前轮内帧序号</summary>
    public int FrameIndexInRound { get; set; }
}

/// <summary>
/// Step6 扫描状态 DTO
/// </summary>
public class CalibScanStatusDto
{
    /// <summary>标定项目 ID</summary>
    public Guid CalibProjectId { get; set; }

    /// <summary>当前状态</summary>
    public CalibScanRunState State { get; set; }

    /// <summary>是否正在运行</summary>
    public bool IsRunning { get; set; }

    /// <summary>启动时间（UTC，未启动时为 null）</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>最近更新时间（UTC）</summary>
    public DateTime LastUpdatedAt { get; set; }

    /// <summary>最近错误信息</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>最近实时指标</summary>
    public CalibScanMetricsDto? LatestMetrics { get; set; }
}

/// <summary>
/// 设置图像增强开关输入
/// </summary>
public class SetCalibScanImageEnhanceInput
{
    /// <summary>标定项目 ID</summary>
    [Required]
    public Guid CalibProjectId { get; set; }

    /// <summary>是否启用 OpenCV CLAHE 图像增强</summary>
    public bool Enabled { get; set; }
}
