using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Workflow;

/// <summary>
/// 项目内工作流任务配置行项（工作流维度：启用与执行顺序）。
/// </summary>
public class WorkflowProjectTask : FullAuditedAggregateRoot<Guid>
{
    /// <summary>项目 ID。</summary>
    public Guid ProjectId { get; private set; }

    /// <summary>工作流 ID。</summary>
    public Guid WorkflowId { get; private set; }

    /// <summary>是否启用。</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>执行顺序。</summary>
    public int OrderNo { get; private set; }

    protected WorkflowProjectTask() { }

    /// <summary>
    /// 创建项目工作流任务配置。
    /// </summary>
    public static WorkflowProjectTask Create(
        Guid id,
        Guid projectId,
        Guid workflowId,
        bool isEnabled,
        int orderNo
    )
    {
        if (projectId == Guid.Empty)
        {
            throw new BusinessException("Workflow.ProjectTask.ProjectId.Empty");
        }

        if (workflowId == Guid.Empty)
        {
            throw new BusinessException("Workflow.ProjectTask.WorkflowId.Empty");
        }

        return new WorkflowProjectTask
        {
            Id = id,
            ProjectId = projectId,
            WorkflowId = workflowId,
            IsEnabled = isEnabled,
            OrderNo = NormalizeOrderNo(orderNo),
        };
    }

    /// <summary>
    /// 更新启用状态。
    /// </summary>
    public void SetEnabled(bool isEnabled)
    {
        IsEnabled = isEnabled;
    }

    /// <summary>
    /// 更新执行顺序。
    /// </summary>
    public void SetOrderNo(int orderNo)
    {
        OrderNo = NormalizeOrderNo(orderNo);
    }

    private static int NormalizeOrderNo(int orderNo)
    {
        if (orderNo < WorkflowProjectTaskConsts.MinOrderNo)
        {
            return WorkflowProjectTaskConsts.MinOrderNo;
        }

        if (orderNo > WorkflowProjectTaskConsts.MaxOrderNo)
        {
            return WorkflowProjectTaskConsts.MaxOrderNo;
        }

        return orderNo;
    }
}
