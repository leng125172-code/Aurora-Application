namespace AuroraStruct3D.Cameras.Dtos;

/// <summary>
/// 相机当前完整状态快照 DTO，前端在页面刷新/返回/重连后用于一次性恢复 UI。
/// </summary>
public class CameraSnapshotStateDto
{
    /// <summary>相机设备 ID</summary>
    public Guid CameraId { get; set; }

    /// <summary>最新 GenICam NodeMap 快照（包含分类、节点值、依赖关系）</summary>
    public CameraNodeMapDto? NodeMap { get; set; }

    /// <summary>当前触发模式数值（0=FreeRunning, 1=Standard 外触发, 2=Software 软触发）</summary>
    public long TriggerMode { get; set; }

    /// <summary>触发模式的语义符号（FreeRunning / Standard / Software / Unknown）</summary>
    public string TriggerModeSymbol { get; set; } = "Unknown";

    /// <summary>是否正在进行 MJPEG/RTP 预览推送</summary>
    public bool IsPreviewing { get; set; }

    /// <summary>底层 TUCam 是否处于采集中（Capture 已启动）</summary>
    public bool IsCapturing { get; set; }

    /// <summary>RTP 推流端点（无预览时为 null）</summary>
    public CameraRtpEndpointDto? RtpEndpoint { get; set; }

    /// <summary>相机当前状态字符串（Online / Offline / Capturing / Error 等）</summary>
    public string CameraStatus { get; set; } = string.Empty;

    /// <summary>快照生成时间（UTC）</summary>
    public DateTime SnapshotAt { get; set; }
}
