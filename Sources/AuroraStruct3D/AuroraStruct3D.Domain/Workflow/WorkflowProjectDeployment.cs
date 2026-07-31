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

    /// <summary>冻结的工作流图 JSON（发布时固化的完整 GraphData 列表）。</summary>
    public string FrozenGraphsJson { get; private set; } = "[]";

    /// <summary>冻结的变量定义 JSON（发布时固化的项目变量默认值列表）。</summary>
    public string FrozenVariablesJson { get; private set; } = "[]";

    /// <summary>激活时间。</summary>
    public DateTime? ActivatedAt { get; private set; }

    /// <summary>激活人。</summary>
    public Guid? ActivatedBy { get; private set; }

    protected WorkflowProjectDeployment() { }

    /// <summary>
    /// 创建已发布部署快照（含冻结的工作流图与变量定义）。
    /// </summary>
    public static WorkflowProjectDeployment CreatePublished(
        Guid id,
        Guid projectId,
        int revision,
        IEnumerable<WorkflowProjectDeploymentItem> items,
        IEnumerable<WorkflowProjectFrozenGraph> frozenGraphs,
        IEnumerable<WorkflowProjectFrozenVariable> frozenVariables,
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
        Check.NotNull(frozenGraphs, nameof(frozenGraphs));
        Check.NotNull(frozenVariables, nameof(frozenVariables));
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

        string frozenGraphsJson = JsonSerializer.Serialize(frozenGraphs.ToList());
        Check.Length(
            frozenGraphsJson,
            nameof(frozenGraphsJson),
            WorkflowProjectDeploymentConsts.MaxFrozenGraphsJsonLength
        );

        string frozenVariablesJson = JsonSerializer.Serialize(frozenVariables.ToList());
        Check.Length(
            frozenVariablesJson,
            nameof(frozenVariablesJson),
            WorkflowProjectDeploymentConsts.MaxFrozenVariablesJsonLength
        );

        return new WorkflowProjectDeployment
        {
            Id = id,
            ProjectId = projectId,
            Revision = revision,
            Status = WorkflowProjectDeploymentStatus.Published,
            SnapshotJson = snapshotJson,
            SnapshotHash = snapshotHash,
            FrozenGraphsJson = frozenGraphsJson,
            FrozenVariablesJson = frozenVariablesJson,
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

    /// <summary>
    /// 从软删除状态恢复为可用部署，并重置激活相关状态。
    /// </summary>
    public void RestoreFromDeleted()
    {
        IsDeleted = false;
        DeletionTime = null;
        DeleterId = null;
        Status = WorkflowProjectDeploymentStatus.Published;
        ActivatedAt = null;
        ActivatedBy = null;
    }

    /// <summary>
    /// 更新部署快照内容（原地修改，不改变版本号与状态）。
    /// </summary>
    /// <returns>内容是否发生变化。</returns>
    public bool UpdatePublishedContent(
        IEnumerable<WorkflowProjectDeploymentItem> items,
        IEnumerable<WorkflowProjectFrozenGraph> frozenGraphs,
        IEnumerable<WorkflowProjectFrozenVariable> frozenVariables,
        string snapshotHash
    )
    {
        Check.NotNull(items, nameof(items));
        Check.NotNull(frozenGraphs, nameof(frozenGraphs));
        Check.NotNull(frozenVariables, nameof(frozenVariables));
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

        string frozenGraphsJson = JsonSerializer.Serialize(frozenGraphs.ToList());
        Check.Length(
            frozenGraphsJson,
            nameof(frozenGraphsJson),
            WorkflowProjectDeploymentConsts.MaxFrozenGraphsJsonLength
        );

        string frozenVariablesJson = JsonSerializer.Serialize(frozenVariables.ToList());
        Check.Length(
            frozenVariablesJson,
            nameof(frozenVariablesJson),
            WorkflowProjectDeploymentConsts.MaxFrozenVariablesJsonLength
        );

        bool changed =
            !string.Equals(SnapshotHash, snapshotHash, StringComparison.Ordinal)
            || !string.Equals(SnapshotJson, snapshotJson, StringComparison.Ordinal)
            || !string.Equals(FrozenGraphsJson, frozenGraphsJson, StringComparison.Ordinal)
            || !string.Equals(FrozenVariablesJson, frozenVariablesJson, StringComparison.Ordinal);

        if (!changed)
        {
            return false;
        }

        SnapshotHash = snapshotHash;
        SnapshotJson = snapshotJson;
        FrozenGraphsJson = frozenGraphsJson;
        FrozenVariablesJson = frozenVariablesJson;
        return true;
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

/// <summary>
/// 部署冻结的工作流图（固化的完整 GraphData）。
/// </summary>
public class WorkflowProjectFrozenGraph
{
    /// <summary>工作流 ID。</summary>
    public Guid WorkflowId { get; set; }

    /// <summary>冻结的工作流画布数据 JSON（GraphData 原文）。</summary>
    public string GraphData { get; set; } = string.Empty;

    /// <summary>冻结的受限 C# 工作流脚本。</summary>
    public string SourceCode { get; set; } = string.Empty;

    /// <summary>冻结脚本的编译程序哈希。</summary>
    public string ProgramHash { get; set; } = string.Empty;

    /// <summary>脚本语言版本。</summary>
    public int LanguageVersion { get; set; } = 1;
}

/// <summary>
/// 部署冻结的变量定义（固化的变量默认值）。
/// </summary>
public class WorkflowProjectFrozenVariable
{
    /// <summary>所属工作流 ID（本地变量的 OwnerWorkflowId）。</summary>
    public Guid OwnerWorkflowId { get; set; }

    /// <summary>变量名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>变量类型名（CLR 全名）。</summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>默认值（Base64 序列化）。</summary>
    public string? DefaultValueJson { get; set; }
}
