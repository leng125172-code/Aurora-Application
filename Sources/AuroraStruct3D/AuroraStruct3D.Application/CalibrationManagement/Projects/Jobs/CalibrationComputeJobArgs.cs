namespace AuroraStruct3D.CalibrationManagement.Projects.Jobs;

/// <summary>
/// 标定计算 Hangfire Job 参数：仅携带工程 ID。
/// </summary>
public class CalibrationComputeJobArgs
{
    /// <summary>待计算的标定工程 ID</summary>
    public Guid CalibrationProjectId { get; set; }
}
