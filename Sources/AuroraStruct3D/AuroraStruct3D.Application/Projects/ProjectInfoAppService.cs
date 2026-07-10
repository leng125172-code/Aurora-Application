using AuroraStruct3D.Permissions;
using AuroraStruct3D.Projects.Dtos;
using AuroraStruct3D.Workflow;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;

namespace AuroraStruct3D.Projects;

/// <summary>
/// 项目管理应用服务实现。
/// 提供项目的增删改查与状态管理功能，
/// 审计日志（创建人/时间、修改人/时间）由 ABP 自动记录。
/// </summary>
[Authorize(ProjectInfoPermissions.Default)]
public class ProjectInfoAppService : AuroraStruct3DAppService, IProjectInfoAppService
{
    private readonly IProjectInfoRepository _projectInfoRepository;
    private readonly IIdentityUserRepository _identityUserRepository;
    private readonly IRepository<WorkflowDefinition, Guid> _workflowRepository;
    private readonly IRepository<WorkflowProjectDeployment, Guid> _deploymentRepository;

    /// <summary>
    /// 初始化 ProjectInfoAppService 实例
    /// </summary>
    public ProjectInfoAppService(
        IProjectInfoRepository projectInfoRepository,
        IIdentityUserRepository identityUserRepository,
        IRepository<WorkflowDefinition, Guid> workflowRepository,
        IRepository<WorkflowProjectDeployment, Guid> deploymentRepository
    )
    {
        _projectInfoRepository = projectInfoRepository;
        _identityUserRepository = identityUserRepository;
        _workflowRepository = workflowRepository;
        _deploymentRepository = deploymentRepository;
    }

    // ─────────────────────────── 查询 ───────────────────────────

    /// <inheritdoc/>
    public async Task<PagedResultDto<ProjectInfoDto>> GetListAsync(GetProjectInfoListInput input)
    {
        long totalCount = await _projectInfoRepository.GetCountAsync(input.Filter, input.Status);

        List<ProjectInfo> items = await _projectInfoRepository.GetListAsync(
            input.SkipCount,
            input.MaxResultCount,
            input.Filter,
            input.Status,
            input.Sorting
        );

        // 批量查询创建人用户名
        Guid[] creatorIds = items
            .Where(p => p.CreatorId.HasValue)
            .Select(p => p.CreatorId!.Value)
            .Distinct()
            .ToArray();

        Dictionary<Guid, string?> userNameMap = new();
        if (creatorIds.Length > 0)
        {
            List<IdentityUser> users = await _identityUserRepository.GetListByIdsAsync(creatorIds);
            userNameMap = users.ToDictionary(u => u.Id, u => u.UserName);
        }

        // 关联查询：批量获取每个项目下的工作流数量
        Guid[] projectIds = items.Select(p => p.Id).ToArray();
        IQueryable<WorkflowDefinition> workflowQueryable =
            await _workflowRepository.GetQueryableAsync();
        List<WorkflowCountResult> countResults = await AsyncExecuter.ToListAsync(
            workflowQueryable
                .Where(w => projectIds.Contains(w.ProjectId))
                .GroupBy(w => w.ProjectId)
                .Select(g => new WorkflowCountResult { ProjectId = g.Key, Count = g.Count() })
        );
        Dictionary<Guid, int> workflowCountMap = countResults.ToDictionary(
            r => r.ProjectId,
            r => r.Count
        );

        // 关联查询：批量获取每个项目当前激活部署（最多一条）。
        IQueryable<WorkflowProjectDeployment> deploymentQueryable =
            await _deploymentRepository.GetQueryableAsync();
        List<ActiveDeploymentResult> activeDeployments = await AsyncExecuter.ToListAsync(
            deploymentQueryable
                .Where(d =>
                    projectIds.Contains(d.ProjectId)
                    && d.Status == WorkflowProjectDeploymentStatus.Activated
                )
                .GroupBy(d => d.ProjectId)
                .Select(g =>
                    g.OrderByDescending(x => x.Revision)
                        .Select(x => new ActiveDeploymentResult
                        {
                            ProjectId = x.ProjectId,
                            DeploymentId = x.Id,
                            Revision = x.Revision,
                        })
                        .First()
                )
        );
        Dictionary<Guid, ActiveDeploymentResult> activeDeploymentMap =
            activeDeployments.ToDictionary(x => x.ProjectId, x => x);

        List<ProjectInfoDto> dtos = items
            .Select(p =>
                MapToDto(
                    p,
                    userNameMap.GetValueOrDefault(p.CreatorId ?? Guid.Empty),
                    workflowCountMap.GetValueOrDefault(p.Id, 0),
                    activeDeploymentMap.GetValueOrDefault(p.Id)
                )
            )
            .ToList();

        return new PagedResultDto<ProjectInfoDto>(totalCount, dtos);
    }

    /// <inheritdoc/>
    public async Task<ProjectInfoDto> GetAsync(Guid id)
    {
        ProjectInfo project = await _projectInfoRepository.GetAsync(id);

        string? creatorUserName = null;
        if (project.CreatorId.HasValue)
        {
            IdentityUser? user = await _identityUserRepository.FindAsync(project.CreatorId.Value);
            creatorUserName = user?.UserName;
        }

        // 关联查询工作流数量
        IQueryable<WorkflowDefinition> workflowQueryable =
            await _workflowRepository.GetQueryableAsync();
        int workflowCount = await AsyncExecuter.CountAsync(
            workflowQueryable,
            w => w.ProjectId == id
        );

        IQueryable<WorkflowProjectDeployment> deploymentQueryable =
            await _deploymentRepository.GetQueryableAsync();
        ActiveDeploymentResult? activeDeployment = await AsyncExecuter.FirstOrDefaultAsync(
            deploymentQueryable
                .Where(d =>
                    d.ProjectId == id && d.Status == WorkflowProjectDeploymentStatus.Activated
                )
                .OrderByDescending(d => d.Revision)
                .Select(d => new ActiveDeploymentResult
                {
                    ProjectId = d.ProjectId,
                    DeploymentId = d.Id,
                    Revision = d.Revision,
                })
        );

        return MapToDto(project, creatorUserName, workflowCount, activeDeployment);
    }

    // ─────────────────────────── 创建 ───────────────────────────

    /// <inheritdoc/>
    [Authorize(ProjectInfoPermissions.Create)]
    public async Task<ProjectInfoDto> CreateAsync(CreateProjectInfoInput input)
    {
        // 唯一性校验
        bool exists = await _projectInfoRepository.ProjectCodeExistsAsync(input.ProjectCode);
        if (exists)
        {
            throw new UserFriendlyException(
                $"项目编号 '{input.ProjectCode}' 已存在，请使用其他编号。"
            );
        }

        ProjectInfo project = ProjectInfo.Create(
            GuidGenerator.Create(),
            input.ProjectCode,
            input.Name,
            input.Version,
            input.Description
        );

        await _projectInfoRepository.InsertAsync(project, autoSave: true);

        return MapToDto(project, null, 0);
    }

    // ─────────────────────────── 修改 ───────────────────────────

    /// <inheritdoc/>
    [Authorize(ProjectInfoPermissions.Update)]
    public async Task<ProjectInfoDto> UpdateAsync(Guid id, UpdateProjectInfoInput input)
    {
        ProjectInfo project = await _projectInfoRepository.GetAsync(id);

        project.Update(input.Name, input.Version, input.Description);

        await _projectInfoRepository.UpdateAsync(project, autoSave: true);

        return MapToDto(project, null, 0);
    }

    /// <inheritdoc/>
    [Authorize(ProjectInfoPermissions.ChangeStatus)]
    public async Task<ProjectInfoDto> ChangeStatusAsync(Guid id, ProjectStatus status)
    {
        ProjectInfo project = await _projectInfoRepository.GetAsync(id);

        project.ChangeStatus(status);

        await _projectInfoRepository.UpdateAsync(project, autoSave: true);

        return MapToDto(project, null, 0);
    }

    // ─────────────────────────── 删除 ───────────────────────────

    /// <inheritdoc/>
    [Authorize(ProjectInfoPermissions.Delete)]
    public async Task DeleteAsync(Guid id)
    {
        await _projectInfoRepository.DeleteAsync(id, autoSave: true);
    }

    // ─────────────────────────── 私有辅助 ───────────────────────────

    /// <summary>
    /// 将实体映射为输出 DTO
    /// </summary>
    private static ProjectInfoDto MapToDto(
        ProjectInfo project,
        string? creatorUserName,
        int workflowCount,
        ActiveDeploymentResult? activeDeployment = null
    )
    {
        return new ProjectInfoDto
        {
            Id = project.Id,
            CreationTime = project.CreationTime,
            CreatorId = project.CreatorId,
            LastModificationTime = project.LastModificationTime,
            LastModifierId = project.LastModifierId,
            IsDeleted = project.IsDeleted,
            DeletionTime = project.DeletionTime,
            DeleterId = project.DeleterId,
            ProjectCode = project.ProjectCode,
            Name = project.Name,
            Version = project.Version,
            Description = project.Description,
            Status = project.Status,
            StatusDisplay = GetStatusDisplay(project.Status),
            CreatorUserName = creatorUserName,
            WorkflowCount = workflowCount,
            HasActiveDeployment = activeDeployment is not null,
            ActiveDeploymentId = activeDeployment?.DeploymentId,
            ActiveDeploymentRevision = activeDeployment?.Revision,
        };
    }

    /// <summary>
    /// 获取项目状态的中文描述
    /// </summary>
    private static string GetStatusDisplay(ProjectStatus status)
    {
        return status switch
        {
            ProjectStatus.Active => "进行中",
            ProjectStatus.Suspended => "已暂停",
            ProjectStatus.Completed => "已完成",
            ProjectStatus.Archived => "已归档",
            _ => status.ToString(),
        };
    }

    /// <summary>工作流数量关联查询结果，用于 GroupBy 投影。</summary>
    private class WorkflowCountResult
    {
        public Guid ProjectId { get; set; }
        public int Count { get; set; }
    }

    /// <summary>项目激活部署关联查询结果。</summary>
    private class ActiveDeploymentResult
    {
        public Guid ProjectId { get; set; }
        public Guid DeploymentId { get; set; }
        public int Revision { get; set; }
    }
}
