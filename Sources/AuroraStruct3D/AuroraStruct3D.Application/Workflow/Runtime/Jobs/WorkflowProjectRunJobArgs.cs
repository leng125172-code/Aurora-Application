namespace AuroraStruct3D.Workflow.Runtime.Jobs;

/// <summary>
/// 项目级工作流运行 Hangfire Job 参数（执行已存在的运行实例）。
/// </summary>
public class WorkflowProjectRunJobArgs
{
    /// <summary>运行 ID。</summary>
    public Guid RunId { get; set; }

    /// <summary>项目 ID。</summary>
    public Guid ProjectId { get; set; }

    /// <summary>来源部署快照 ID。</summary>
    public Guid DeploymentId { get; set; }

    /// <summary>工作流 ID 列表。</summary>
    public List<Guid> WorkflowIds { get; set; } = new();

    /// <summary>单个工作流失败后是否继续。</summary>
    public bool ContinueOnError { get; set; }
}

/// <summary>
/// 项目级工作流周期运行 Hangfire RecurringJob 参数（每次触发创建新的运行实例）。
/// </summary>
public class WorkflowProjectRunRecurringArgs
{
    /// <summary>项目 ID。</summary>
    public Guid ProjectId { get; set; }

    /// <summary>来源部署快照 ID。</summary>
    public Guid DeploymentId { get; set; }

    /// <summary>来源部署版本号。</summary>
    public int DeploymentRevision { get; set; }

    /// <summary>单个工作流失败后是否继续。</summary>
    public bool ContinueOnError { get; set; }

    /// <summary>运行名称前缀。</summary>
    public string NamePrefix { get; set; } = string.Empty;
}
