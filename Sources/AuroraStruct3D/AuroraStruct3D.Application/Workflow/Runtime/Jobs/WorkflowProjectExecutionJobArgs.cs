namespace AuroraStruct3D.Workflow.Runtime.Jobs;

/// <summary>
/// 项目级工作流执行 Hangfire Job 参数。
/// </summary>
public class WorkflowProjectExecutionJobArgs
{
    /// <summary>任务 ID。</summary>
    public Guid TaskId { get; set; }

    /// <summary>项目 ID。</summary>
    public Guid ProjectId { get; set; }

    /// <summary>工作流 ID 列表。</summary>
    public List<Guid> WorkflowIds { get; set; } = new();

    /// <summary>在线变量池模式。</summary>
    public bool UseOnlineVariablePool { get; set; }

    /// <summary>在线变量池实例 ID。</summary>
    public Guid? RuntimeInstanceId { get; set; }

    /// <summary>变量读取超时毫秒。</summary>
    public int VariableReadTimeoutMs { get; set; } = 5000;

    /// <summary>单个工作流失败后是否继续。</summary>
    public bool ContinueOnError { get; set; }
}
