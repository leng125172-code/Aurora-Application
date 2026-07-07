using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Workflow;

/// <summary>
/// 项目内工作流绑定配置（启用与执行顺序）。
/// </summary>
public class WorkflowProjectBinding : FullAuditedAggregateRoot<Guid>
{
    /// <summary>项目 ID。</summary>
    public Guid ProjectId { get; private set; }

    /// <summary>工作流 ID。</summary>
    public Guid WorkflowId { get; private set; }

    /// <summary>是否启用。</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>执行顺序。</summary>
    public int OrderNo { get; private set; }

    protected WorkflowProjectBinding() { }

    /// <summary>
    /// 创建项目工作流绑定。
    /// </summary>
    public static WorkflowProjectBinding Create(
        Guid id,
        Guid projectId,
        Guid workflowId,
        bool isEnabled,
        int orderNo
    )
    {
        if (projectId == Guid.Empty)
        {
            throw new BusinessException("Workflow.ProjectBinding.ProjectId.Empty");
        }

        if (workflowId == Guid.Empty)
        {
            throw new BusinessException("Workflow.ProjectBinding.WorkflowId.Empty");
        }

        return new WorkflowProjectBinding
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
        if (orderNo < WorkflowProjectBindingConsts.MinOrderNo)
        {
            return WorkflowProjectBindingConsts.MinOrderNo;
        }

        if (orderNo > WorkflowProjectBindingConsts.MaxOrderNo)
        {
            return WorkflowProjectBindingConsts.MaxOrderNo;
        }

        return orderNo;
    }
}
