namespace AuroraStruct3D.Workflow;

/// <summary>
/// 项目运行触发启动类型。
/// </summary>
public enum WorkflowProjectRunStartType
{
    /// <summary>立即启动（单次执行）。</summary>
    Immediate = 0,

    /// <summary>周期启动（按间隔重复执行激活部署）。</summary>
    Cyclic = 1,
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
/// 项目级工作流运行状态。
/// </summary>
public enum WorkflowProjectRunStatus
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
/// 运行内单工作流执行状态。
/// </summary>
public enum WorkflowProjectRunItemStatus
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
