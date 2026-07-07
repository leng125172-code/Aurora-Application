namespace AuroraStruct3D.Workflow;

/// <summary>
/// 项目任务触发启动类型。
/// </summary>
public enum WorkflowProjectTaskStartType
{
    /// <summary>立即启动。</summary>
    Immediate = 0,
}

/// <summary>
/// 项目部署快照状态。
/// </summary>
public enum WorkflowProjectDeploymentStatus
{
    /// <summary>草稿。</summary>
    Draft = 0,

    /// <summary>已发布。</summary>
    Published = 1,

    /// <summary>已激活。</summary>
    Activated = 2,

    /// <summary>已归档。</summary>
    Archived = 3,
}

/// <summary>
/// 项目级工作流任务状态。
/// </summary>
public enum WorkflowProjectTaskStatus
{
    /// <summary>已入队。</summary>
    Queued = 0,

    /// <summary>执行中。</summary>
    Running = 1,

    /// <summary>成功。</summary>
    Succeeded = 2,

    /// <summary>失败。</summary>
    Failed = 3,

    /// <summary>已取消。</summary>
    Canceled = 4,
}

/// <summary>
/// 任务内单工作流执行状态。
/// </summary>
public enum WorkflowProjectTaskItemStatus
{
    /// <summary>等待执行。</summary>
    Pending = 0,

    /// <summary>执行中。</summary>
    Running = 1,

    /// <summary>成功。</summary>
    Succeeded = 2,

    /// <summary>失败。</summary>
    Failed = 3,

    /// <summary>已跳过。</summary>
    Skipped = 4,

    /// <summary>已取消。</summary>
    Canceled = 5,
}
