using System.Text.Json;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Workflow;

/// <summary>
/// 项目级工作流任务聚合根（1 任务 = 1 项目 = 多个工作流）。
/// </summary>
public class WorkflowProjectTask : FullAuditedAggregateRoot<Guid>
{
    /// <summary>项目 ID。</summary>
    public Guid ProjectId { get; private set; }

    /// <summary>任务名称。</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Hangfire JobId。</summary>
    public string? HangfireJobId { get; private set; }

    /// <summary>任务状态。</summary>
    public WorkflowProjectTaskStatus Status { get; private set; }

    /// <summary>任务类型（触发启动类型）。</summary>
    public WorkflowProjectTaskStartType StartType { get; private set; }

    /// <summary>绑定的部署快照 ID；legacy 回退模式下为空。</summary>
    public Guid? DeploymentId { get; private set; }

    /// <summary>绑定的部署版本号；legacy 回退模式下为空。</summary>
    public int? DeploymentRevision { get; private set; }

    /// <summary>是否继续执行后续工作流。</summary>
    public bool ContinueOnError { get; private set; }

    /// <summary>是否在线变量池模式。</summary>
    public bool UseOnlineVariablePool { get; private set; }

    /// <summary>在线变量池实例 ID。</summary>
    public Guid? RuntimeInstanceId { get; private set; }

    /// <summary>变量读取超时毫秒。</summary>
    public int VariableReadTimeoutMs { get; private set; }

    /// <summary>工作流 ID 列表 JSON。</summary>
    public string WorkflowIdsJson { get; private set; } = "[]";

    /// <summary>工作流执行结果 JSON。</summary>
    public string ResultsJson { get; private set; } = "[]";

    /// <summary>已执行数量。</summary>
    public int ExecutedCount { get; private set; }

    /// <summary>成功数量。</summary>
    public int SuccessCount { get; private set; }

    /// <summary>失败数量。</summary>
    public int FailedCount { get; private set; }

    /// <summary>开始时间。</summary>
    public DateTime? StartedAt { get; private set; }

    /// <summary>结束时间。</summary>
    public DateTime? FinishedAt { get; private set; }

    /// <summary>错误信息。</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>取消标记。</summary>
    public bool IsCancelRequested { get; private set; }

    /// <summary>无参构造（EF）。</summary>
    protected WorkflowProjectTask() { }

    /// <summary>
    /// 创建任务。
    /// </summary>
    public static WorkflowProjectTask Create(
        Guid id,
        Guid projectId,
        string name,
        WorkflowProjectTaskStartType startType,
        Guid? deploymentId,
        int? deploymentRevision,
        IEnumerable<Guid> workflowIds,
        bool continueOnError,
        bool useOnlineVariablePool,
        Guid? runtimeInstanceId,
        int variableReadTimeoutMs
    )
    {
        Check.NotNullOrWhiteSpace(name, nameof(name), WorkflowProjectTaskConsts.MaxNameLength);

        string workflowIdsJson = JsonSerializer.Serialize(workflowIds.Distinct().ToList());
        Check.Length(
            workflowIdsJson,
            nameof(workflowIdsJson),
            WorkflowProjectTaskConsts.MaxWorkflowIdsJsonLength
        );

        return new WorkflowProjectTask
        {
            Id = id,
            ProjectId = projectId,
            Name = name,
            Status = WorkflowProjectTaskStatus.Queued,
            StartType = startType,
            DeploymentId = deploymentId,
            DeploymentRevision = deploymentRevision,
            ContinueOnError = continueOnError,
            UseOnlineVariablePool = useOnlineVariablePool,
            RuntimeInstanceId = runtimeInstanceId,
            VariableReadTimeoutMs = Math.Max(variableReadTimeoutMs, 0),
            WorkflowIdsJson = workflowIdsJson,
            ResultsJson = "[]",
            ExecutedCount = 0,
            SuccessCount = 0,
            FailedCount = 0,
            IsCancelRequested = false,
        };
    }

    /// <summary>
    /// 设置 Hangfire JobId。
    /// </summary>
    public void SetHangfireJobId(string jobId)
    {
        Check.NotNullOrWhiteSpace(
            jobId,
            nameof(jobId),
            WorkflowProjectTaskConsts.MaxHangfireJobIdLength
        );
        HangfireJobId = jobId;
    }

    /// <summary>
    /// 标记开始。
    /// </summary>
    public void MarkRunning(DateTime now)
    {
        Status = WorkflowProjectTaskStatus.Running;
        StartedAt ??= now;
        ErrorMessage = null;
    }

    /// <summary>
    /// 请求取消。
    /// </summary>
    public void RequestCancel()
    {
        IsCancelRequested = true;
    }

    /// <summary>
    /// 修改任务名称。
    /// </summary>
    public void UpdateName(string name)
    {
        Check.NotNullOrWhiteSpace(name, nameof(name), WorkflowProjectTaskConsts.MaxNameLength);
        Name = name;
    }

    /// <summary>
    /// 修改失败后是否继续执行。
    /// </summary>
    public void UpdateContinueOnError(bool continueOnError)
    {
        ContinueOnError = continueOnError;
    }

    /// <summary>
    /// 更新执行快照。
    /// </summary>
    public void UpdateProgress(
        IEnumerable<WorkflowProjectTaskItemResult> items,
        int executedCount,
        int successCount,
        int failedCount
    )
    {
        string resultsJson = JsonSerializer.Serialize(items);
        Check.Length(
            resultsJson,
            nameof(resultsJson),
            WorkflowProjectTaskConsts.MaxResultsJsonLength
        );

        ResultsJson = resultsJson;
        ExecutedCount = Math.Max(executedCount, 0);
        SuccessCount = Math.Max(successCount, 0);
        FailedCount = Math.Max(failedCount, 0);
    }

    /// <summary>
    /// 标记成功完成。
    /// </summary>
    public void MarkSucceeded(DateTime now)
    {
        Status = WorkflowProjectTaskStatus.Succeeded;
        FinishedAt = now;
        ErrorMessage = null;
    }

    /// <summary>
    /// 标记失败。
    /// </summary>
    public void MarkFailed(DateTime now, string? errorMessage)
    {
        Status = WorkflowProjectTaskStatus.Failed;
        FinishedAt = now;
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage)
            ? null
            : errorMessage[
                ..Math.Min(errorMessage.Length, WorkflowProjectTaskConsts.MaxErrorLength)
            ];
    }

    /// <summary>
    /// 标记取消。
    /// </summary>
    public void MarkCanceled(DateTime now)
    {
        Status = WorkflowProjectTaskStatus.Canceled;
        FinishedAt = now;
        ErrorMessage = null;
    }
}

/// <summary>
/// 任务内单工作流执行结果。
/// </summary>
public class WorkflowProjectTaskItemResult
{
    /// <summary>工作流 ID。</summary>
    public Guid WorkflowId { get; set; }

    /// <summary>执行状态。</summary>
    public WorkflowProjectTaskItemStatus Status { get; set; }

    /// <summary>执行会话 ID。</summary>
    public Guid? ExecutionId { get; set; }

    /// <summary>错误信息。</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>开始时间。</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>结束时间。</summary>
    public DateTime? FinishedAt { get; set; }
}
