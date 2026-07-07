using System.Text.Json;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Workflow;

/// <summary>
/// 项目部署快照（类似写入 PLC 的发布版本）。
/// </summary>
public class WorkflowProjectDeployment : FullAuditedAggregateRoot<Guid>
{
    /// <summary>项目 ID。</summary>
    public Guid ProjectId { get; private set; }

    /// <summary>版本号。</summary>
    public int Revision { get; private set; }

    /// <summary>部署状态。</summary>
    public WorkflowProjectDeploymentStatus Status { get; private set; }

    /// <summary>快照 JSON。</summary>
    public string SnapshotJson { get; private set; } = "[]";

    /// <summary>快照哈希。</summary>
    public string SnapshotHash { get; private set; } = string.Empty;

    /// <summary>激活时间。</summary>
    public DateTime? ActivatedAt { get; private set; }

    /// <summary>激活人。</summary>
    public Guid? ActivatedBy { get; private set; }

    protected WorkflowProjectDeployment() { }

    /// <summary>
    /// 创建已发布部署快照。
    /// </summary>
    public static WorkflowProjectDeployment CreatePublished(
        Guid id,
        Guid projectId,
        int revision,
        IEnumerable<WorkflowProjectDeploymentItem> items,
        string snapshotHash
    )
    {
        if (projectId == Guid.Empty)
        {
            throw new BusinessException("Workflow.ProjectDeployment.ProjectId.Empty");
        }

        if (revision <= 0)
        {
            throw new BusinessException("Workflow.ProjectDeployment.Revision.Invalid");
        }

        Check.NotNull(items, nameof(items));
        Check.NotNullOrWhiteSpace(
            snapshotHash,
            nameof(snapshotHash),
            WorkflowProjectDeploymentConsts.MaxSnapshotHashLength
        );

        string snapshotJson = JsonSerializer.Serialize(items.ToList());
        Check.Length(
            snapshotJson,
            nameof(snapshotJson),
            WorkflowProjectDeploymentConsts.MaxSnapshotJsonLength
        );

        return new WorkflowProjectDeployment
        {
            Id = id,
            ProjectId = projectId,
            Revision = revision,
            Status = WorkflowProjectDeploymentStatus.Published,
            SnapshotJson = snapshotJson,
            SnapshotHash = snapshotHash,
        };
    }

    /// <summary>
    /// 标记为激活。
    /// </summary>
    public void Activate(Guid? userId, DateTime now)
    {
        Status = WorkflowProjectDeploymentStatus.Activated;
        ActivatedBy = userId;
        ActivatedAt = now;
    }

    /// <summary>
    /// 标记为归档。
    /// </summary>
    public void Archive()
    {
        Status = WorkflowProjectDeploymentStatus.Archived;
    }
}

/// <summary>
/// 项目部署快照项。
/// </summary>
public class WorkflowProjectDeploymentItem
{
    /// <summary>工作流 ID。</summary>
    public Guid WorkflowId { get; set; }

    /// <summary>工作流名称。</summary>
    public string WorkflowName { get; set; } = string.Empty;

    /// <summary>执行顺序。</summary>
    public int OrderNo { get; set; }

    /// <summary>发布时的图数据指纹。</summary>
    public string GraphHash { get; set; } = string.Empty;
}
