using AuroraStruct3D.Permissions;
using AuroraStruct3D.Projects.Dtos;
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

    /// <summary>
    /// 初始化 ProjectInfoAppService 实例
    /// </summary>
    public ProjectInfoAppService(
        IProjectInfoRepository projectInfoRepository,
        IIdentityUserRepository identityUserRepository
    )
    {
        _projectInfoRepository = projectInfoRepository;
        _identityUserRepository = identityUserRepository;
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

        List<ProjectInfoDto> dtos = items
            .Select(p => MapToDto(p, userNameMap.GetValueOrDefault(p.CreatorId ?? Guid.Empty)))
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

        return MapToDto(project, creatorUserName);
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

        return MapToDto(project, null);
    }

    // ─────────────────────────── 修改 ───────────────────────────

    /// <inheritdoc/>
    [Authorize(ProjectInfoPermissions.Update)]
    public async Task<ProjectInfoDto> UpdateAsync(Guid id, UpdateProjectInfoInput input)
    {
        ProjectInfo project = await _projectInfoRepository.GetAsync(id);

        project.Update(input.Name, input.Version, input.Description);

        await _projectInfoRepository.UpdateAsync(project, autoSave: true);

        return MapToDto(project, null);
    }

    /// <inheritdoc/>
    [Authorize(ProjectInfoPermissions.ChangeStatus)]
    public async Task<ProjectInfoDto> ChangeStatusAsync(Guid id, ProjectStatus status)
    {
        ProjectInfo project = await _projectInfoRepository.GetAsync(id);

        project.ChangeStatus(status);

        await _projectInfoRepository.UpdateAsync(project, autoSave: true);

        return MapToDto(project, null);
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
    private static ProjectInfoDto MapToDto(ProjectInfo project, string? creatorUserName)
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
}
