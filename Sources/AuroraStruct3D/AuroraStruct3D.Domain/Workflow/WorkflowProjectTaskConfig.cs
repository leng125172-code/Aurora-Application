using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Workflow;

/// <summary>
/// 项目级工作流任务触发配置（项目维度：触发类型与周期参数）。
/// </summary>
public class WorkflowProjectTaskConfig : FullAuditedAggregateRoot<Guid>
{
    /// <summary>项目 ID。</summary>
    public Guid ProjectId { get; private set; }

    /// <summary>触发类型。</summary>
    public WorkflowProjectTaskType TaskType { get; private set; }

    /// <summary>周期间隔（秒）；仅 <see cref="WorkflowProjectTaskType.Cyclic"/> 有效。</summary>
    public int? CycleIntervalSeconds { get; private set; }

    protected WorkflowProjectTaskConfig() { }

    /// <summary>
    /// 创建项目级任务触发配置。
    /// </summary>
    public static WorkflowProjectTaskConfig Create(
        Guid id,
        Guid projectId,
        WorkflowProjectTaskType taskType = WorkflowProjectTaskType.Immediate,
        int? cycleIntervalSeconds = null
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
            TaskType = taskType,
            CycleIntervalSeconds = NormalizeCycleInterval(taskType, cycleIntervalSeconds),
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
