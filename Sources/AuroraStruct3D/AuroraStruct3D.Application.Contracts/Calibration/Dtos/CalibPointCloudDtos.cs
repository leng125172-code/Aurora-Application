using System.ComponentModel.DataAnnotations;

namespace AuroraStruct3D.Calibration.Dtos;

/// <summary>
/// Step7 触发点云生成输入
/// </summary>
public class GeneratePointCloudInput
{
    /// <summary>标定项目 ID</summary>
    [Required]
    public Guid CalibProjectId { get; set; }
}

/// <summary>
/// Step7 点云生成运行状态
/// </summary>
public enum PointCloudRunState
{
    /// <summary>空闲</summary>
    Idle = 0,

    /// <summary>生成中</summary>
    Running = 1,

    /// <summary>已完成</summary>
    Completed = 2,

    /// <summary>失败</summary>
    Failed = 3,
}

/// <summary>
/// Step7 点云生成状态 DTO
/// </summary>
public class PointCloudStatusDto
{
    /// <summary>标定项目 ID</summary>
    public Guid CalibProjectId { get; set; }

    /// <summary>当前状态</summary>
    public PointCloudRunState State { get; set; }

    /// <summary>是否正在生成</summary>
    public bool IsRunning { get; set; }

    /// <summary>生成进度（0~100）</summary>
    public int Progress { get; set; }

    /// <summary>进度描述文字</summary>
    public string? ProgressMessage { get; set; }

    /// <summary>生成开始时间（UTC，未启动时为 null）</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>最近更新时间（UTC）</summary>
    public DateTime LastUpdatedAt { get; set; }

    /// <summary>最近错误信息</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>生成完成后的 PLY 文件下载 URL（未完成或失败时为 null）</summary>
    public string? PlyDownloadUrl { get; set; }

    /// <summary>PLY 文件大小（字节，未完成时为 null）</summary>
    public long? PlyFileSizeBytes { get; set; }
}
