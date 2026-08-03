using System.Text.Json;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Workflow;

/// <summary>
/// 项目级工作流运行实例聚合根（1 运行 = 1 项目 = 多个工作流，来源于激活的部署快照）。
/// </summary>
public class WorkflowProjectRun : FullAuditedAggregateRoot<Guid>
{
    /// <summary>项目 ID。</summary>
    public Guid ProjectId { get; private set; }

    /// <summary>运行名称。</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Hangfire JobId。</summary>
    public string? HangfireJobId { get; private set; }

    /// <summary>运行状态。</summary>
    public WorkflowProjectRunStatus Status { get; private set; }

    /// <summary>触发启动类型。</summary>
    public WorkflowProjectRunStartType StartType { get; private set; }

    /// <summary>周期间隔（秒）；仅 Cyclic 有效。</summary>
    public int? CycleIntervalSeconds { get; private set; }

    /// <summary>来源部署快照 ID（强制部署后必有）。</summary>
    public Guid? DeploymentId { get; private set; }

    /// <summary>来源部署版本号（强制部署后必有）。</summary>
    public int? DeploymentRevision { get; private set; }

    /// <summary>是否继续执行后续工作流。</summary>
    public bool ContinueOnError { get; private set; }

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

    public WorkflowInspectionDecision InspectionDecision { get; private set; }
    public WorkflowPlcHandshakeErrorCode InspectionErrorCode { get; private set; }
    public string? InspectionErrorMessage { get; private set; }

    /// <summary>无参构造（EF）。</summary>
    protected WorkflowProjectRun() { }

    /// <summary>
    /// 创建运行实例。
    /// </summary>
    public static WorkflowProjectRun Create(
        Guid id,
        Guid projectId,
        string name,
        WorkflowProjectRunStartType startType,
        int? cycleIntervalSeconds,
        Guid deploymentId,
        int deploymentRevision,
        IEnumerable<Guid> workflowIds,
        bool continueOnError
    )
    {
        Check.NotNullOrWhiteSpace(name, nameof(name), WorkflowProjectRunConsts.MaxNameLength);

        string workflowIdsJson = JsonSerializer.Serialize(workflowIds.Distinct().ToList());
        Check.Length(
            workflowIdsJson,
            nameof(workflowIdsJson),
            WorkflowProjectRunConsts.MaxWorkflowIdsJsonLength
        );

        return new WorkflowProjectRun
        {
            Id = id,
            ProjectId = projectId,
            Name = name,
            Status = WorkflowProjectRunStatus.Queued,
            StartType = startType,
            CycleIntervalSeconds = cycleIntervalSeconds,
            DeploymentId = deploymentId,
            DeploymentRevision = deploymentRevision,
            ContinueOnError = continueOnError,
            WorkflowIdsJson = workflowIdsJson,
            ResultsJson = "[]",
            ExecutedCount = 0,
            SuccessCount = 0,
            FailedCount = 0,
            IsCancelRequested = false,
            InspectionDecision = WorkflowInspectionDecision.None,
        };
    }

    public void SetInspectionResult(WorkflowInspectionDecision decision, WorkflowPlcHandshakeErrorCode errorCode = WorkflowPlcHandshakeErrorCode.None, string? errorMessage = null)
    {
        InspectionDecision = decision;
        InspectionErrorCode = errorCode;
        InspectionErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage[..Math.Min(errorMessage.Length, WorkflowProjectRunConsts.MaxErrorLength)];
    }

    /// <summary>
    /// 设置 Hangfire JobId。
    /// </summary>
    public void SetHangfireJobId(string jobId)
    {
        Check.NotNullOrWhiteSpace(
            jobId,
            nameof(jobId),
            WorkflowProjectRunConsts.MaxHangfireJobIdLength
        );
        HangfireJobId = jobId;
    }

    /// <summary>
    /// 标记开始。
    /// </summary>
    public void MarkRunning(DateTime now)
    {
        Status = WorkflowProjectRunStatus.Running;
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
    /// 修改运行名称。
    /// </summary>
    public void UpdateName(string name)
    {
        Check.NotNullOrWhiteSpace(name, nameof(name), WorkflowProjectRunConsts.MaxNameLength);
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
        IEnumerable<WorkflowProjectRunItemResult> items,
        int executedCount,
        int successCount,
        int failedCount
    )
    {
        string resultsJson = JsonSerializer.Serialize(items);
        Check.Length(
            resultsJson,
            nameof(resultsJson),
            WorkflowProjectRunConsts.MaxResultsJsonLength
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
        Status = WorkflowProjectRunStatus.Succeeded;
        FinishedAt = now;
        ErrorMessage = null;
    }

    /// <summary>
    /// 标记失败。
    /// </summary>
    public void MarkFailed(DateTime now, string? errorMessage)
    {
        Status = WorkflowProjectRunStatus.Failed;
        FinishedAt = now;
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage)
            ? null
            : errorMessage[
                ..Math.Min(errorMessage.Length, WorkflowProjectRunConsts.MaxErrorLength)
            ];
    }

    /// <summary>
    /// 标记取消。
    /// </summary>
    public void MarkCanceled(DateTime now)
    {
        Status = WorkflowProjectRunStatus.Canceled;
        FinishedAt = now;
        ErrorMessage = null;
    }
}

/// <summary>
/// 运行内单工作流执行结果。
/// </summary>
public class WorkflowProjectRunItemResult
{
    /// <summary>工作流 ID。</summary>
    public Guid WorkflowId { get; set; }

    /// <summary>执行状态。</summary>
    public WorkflowProjectRunItemStatus Status { get; set; }

    /// <summary>执行会话 ID。</summary>
    public Guid? ExecutionId { get; set; }

    /// <summary>错误信息。</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>开始时间。</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>结束时间。</summary>
    public DateTime? FinishedAt { get; set; }
}
