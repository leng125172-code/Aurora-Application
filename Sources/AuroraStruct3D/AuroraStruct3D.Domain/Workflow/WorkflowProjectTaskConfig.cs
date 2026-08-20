using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Workflow;

/// <summary>
/// 项目级工作流任务触发配置（项目维度：触发类型与周期参数）。
/// </summary>
public class WorkflowProjectTaskConfig : FullAuditedAggregateRoot<Guid>
{
    public const int MaxNameLength = 128;

    /// <summary>任务名称，用于同项目下区分多条任务。</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>项目 ID。</summary>
    public Guid ProjectId { get; private set; }

    /// <summary>触发类型。</summary>
    public WorkflowProjectTaskType TaskType { get; private set; }

    /// <summary>周期间隔（秒）；仅 <see cref="WorkflowProjectTaskType.Cyclic"/> 有效。</summary>
    public int? CycleIntervalSeconds { get; private set; }

    /// <summary>正式任务最终 OK/NG 判定所在工作流。</summary>
    public Guid? ResultWorkflowId { get; private set; }

    /// <summary>正式任务最终 OK/NG 判定布尔变量名。</summary>
    public string? ResultVariableName { get; private set; }

    public WorkflowProjectRunOnErrorAction OnErrorAction { get; private set; } =
        WorkflowProjectRunOnErrorAction.StopRun;

    /// <summary>项目任务是否启用。</summary>
    public bool IsEnabled { get; private set; } = true;

    protected WorkflowProjectTaskConfig() { }

    /// <summary>
    /// 创建项目级任务触发配置。
    /// </summary>
    public static WorkflowProjectTaskConfig Create(
        Guid id,
        Guid projectId,
        WorkflowProjectTaskType taskType = WorkflowProjectTaskType.Immediate,
        int? cycleIntervalSeconds = null,
        bool isEnabled = true,
        string name = "默认任务"
    )
    {
        if (projectId == Guid.Empty)
        {
            throw new BusinessException("Workflow.ProjectTaskConfig.ProjectId.Empty");
        }

        return new WorkflowProjectTaskConfig
        {
            Id = id,
            ProjectId = projectId,
            Name = Check.NotNullOrWhiteSpace(name, nameof(name), MaxNameLength).Trim(),
            TaskType = taskType,
            CycleIntervalSeconds = NormalizeCycleInterval(taskType, cycleIntervalSeconds),
            IsEnabled = isEnabled,
        };
    }

    /// <summary>
    /// 更新触发类型与周期间隔。
    /// </summary>
    public void SetTrigger(WorkflowProjectTaskType taskType, int? cycleIntervalSeconds)
    {
        TaskType = taskType;
        CycleIntervalSeconds = NormalizeCycleInterval(taskType, cycleIntervalSeconds);
    }

    public void SetEnabled(bool isEnabled) => IsEnabled = isEnabled;

    public void SetName(string name) =>
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), MaxNameLength).Trim();

    public void SetResultBinding(Guid? workflowId, string? variableName)
    {
        if (workflowId is null || workflowId == Guid.Empty || string.IsNullOrWhiteSpace(variableName))
        {
            ResultWorkflowId = null;
            ResultVariableName = null;
            return;
        }
        ResultWorkflowId = workflowId;
        ResultVariableName = Check.NotNullOrWhiteSpace(variableName, nameof(variableName), 128);
    }

    public void SetOnErrorAction(WorkflowProjectRunOnErrorAction action)
    {
        if (!Enum.IsDefined(action))
            throw new BusinessException("Workflow.ProjectTask.OnErrorAction.Invalid");
        OnErrorAction = action;
    }

    private static int? NormalizeCycleInterval(
        WorkflowProjectTaskType taskType,
        int? cycleIntervalSeconds
    )
    {
        if (taskType != WorkflowProjectTaskType.Cyclic)
        {
            return null;
        }

        if (cycleIntervalSeconds is null or <= 0)
        {
            throw new BusinessException("Workflow.ProjectTask.CycleInterval.Invalid");
        }

        return cycleIntervalSeconds;
    }
}
