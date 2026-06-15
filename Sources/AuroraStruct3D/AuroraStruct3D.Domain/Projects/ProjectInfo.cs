using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Projects;

/// <summary>
/// 项目主表聚合根。
/// 记录项目的基本信息，包括编号、名称、版本、描述等。
/// 审计信息（创建人/时间、修改人/时间）由 FullAuditedAggregateRoot 自动记录。
///
/// 数据库表：AbpProProjects
/// </summary>
public class ProjectInfo : FullAuditedAggregateRoot<Guid>
{
    // ─────────────────────────── 基本信息 ───────────────────────────

    /// <summary>项目编号（唯一，如 PRJ-2024-001）</summary>
    public string ProjectCode { get; private set; } = null!;

    /// <summary>项目名称</summary>
    public string Name { get; private set; } = null!;

    /// <summary>项目版本号（如 1.0.0）</summary>
    public string Version { get; private set; } = null!;

    /// <summary>项目描述</summary>
    public string? Description { get; private set; }

    /// <summary>项目状态</summary>
    public ProjectStatus Status { get; private set; }

    // ─────────────────────────── 构造函数 ───────────────────────────

    /// <summary>EF Core 所需的无参构造函数（不得直接使用）</summary>
    protected ProjectInfo() { }

    /// <summary>
    /// 工厂方法：创建新的项目记录。
    /// </summary>
    /// <param name="id">唯一标识</param>
    /// <param name="projectCode">项目编号</param>
    /// <param name="name">项目名称</param>
    /// <param name="version">版本号</param>
    /// <param name="description">项目描述（可为空）</param>
    public static ProjectInfo Create(
        Guid id,
        string projectCode,
        string name,
        string version,
        string? description = null
    )
    {
        Check.NotNullOrWhiteSpace(
            projectCode,
            nameof(projectCode),
            ProjectInfoConsts.MaxProjectCodeLength
        );
        Check.NotNullOrWhiteSpace(name, nameof(name), ProjectInfoConsts.MaxNameLength);
        Check.NotNullOrWhiteSpace(version, nameof(version), ProjectInfoConsts.MaxVersionLength);

        return new ProjectInfo
        {
            Id = id,
            ProjectCode = projectCode,
            Name = name,
            Version = version,
            Description = description,
            Status = ProjectStatus.Active,
        };
    }

    /// <summary>
    /// 更新项目基本信息。
    /// </summary>
    /// <param name="name">项目名称</param>
    /// <param name="version">版本号</param>
    /// <param name="description">项目描述</param>
    public void Update(string name, string version, string? description)
    {
        Check.NotNullOrWhiteSpace(name, nameof(name), ProjectInfoConsts.MaxNameLength);
        Check.NotNullOrWhiteSpace(version, nameof(version), ProjectInfoConsts.MaxVersionLength);

        Name = name;
        Version = version;
        Description = description;
    }

    /// <summary>
    /// 变更项目状态。
    /// </summary>
    /// <param name="status">目标状态</param>
    public void ChangeStatus(ProjectStatus status)
    {
        Status = status;
    }
}
