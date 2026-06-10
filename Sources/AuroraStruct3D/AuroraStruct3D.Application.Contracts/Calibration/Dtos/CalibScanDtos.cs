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

    /// <summary>扫描模式（为空时按项目设备类型自动推断）</summary>
    public CalibScanMode? ScanMode { get; set; }
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

    /// <summary>深度有效率（0~1）</summary>
    public double DepthValidRate { get; set; }

    /// <summary>重建置信度（0~1）</summary>
    public double Confidence { get; set; }

    /// <summary>当前帧序号</summary>
    public long FrameIndex { get; set; }

    /// <summary>指标时间戳（UTC）</summary>
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Step6 扫描状态 DTO
/// </summary>
public class CalibScanStatusDto
{
    /// <summary>标定项目 ID</summary>
    public Guid CalibProjectId { get; set; }

    /// <summary>扫描模式</summary>
    public CalibScanMode ScanMode { get; set; }

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
