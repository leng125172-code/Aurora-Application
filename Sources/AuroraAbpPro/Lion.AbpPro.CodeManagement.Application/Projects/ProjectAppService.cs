using Lion.AbpPro.CodeManagement.EntityModels;

namespace Lion.AbpPro.CodeManagement.Projects;

[Authorize(policy: CodeManagementPermissions.CodeManagement.Project.Default)]
public class ProjectAppService : CodeManagementAppService, IProjectAppService
{
    private readonly ProjectManager _projectManager;
    private readonly EntityModelManager _entityModelManager;

    public ProjectAppService(ProjectManager projectManager, EntityModelManager entityModelManager)
    {
        _projectManager = projectManager;
        _entityModelManager = entityModelManager;
    }

    public async Task<List<ProjectDto>> AllAsync()
    {
        return await _projectManager.GetListAsync(maxResultCount: int.MaxValue);
    }

    public async Task<PagedResultDto<ProjectDto>> PageAsync(PageProjectInput input)
    {
        var result = new PagedResultDto<ProjectDto>();
        var totalCount = await _projectManager.GetCountAsync(input.Filter);
        result.TotalCount = totalCount;
        if (totalCount <= 0)
            return result;

        var list = await _projectManager.GetListAsync(
            input.Filter,
            input.PageSize,
            input.SkipCount,
            false
        );
        result.Items = list;

        return result;
    }

    [Authorize(policy: CodeManagementPermissions.CodeManagement.Project.Create)]
    public Task CreateAsync(CreateProjectInput input)
    {
        return _projectManager.CreateAsync(
            input.CompanyName,
            input.ProjectName,
            input.SupportTenant,
            input.Owner,
            input.Remark
        );
    }

    [Authorize(policy: CodeManagementPermissions.CodeManagement.Project.Update)]
    public Task UpdateAsync(UpdateProjectInput input)
    {
        return _projectManager.UpdateAsync(
            input.Id,
            input.CompanyName,
            input.ProjectName,
            input.SupportTenant,
            input.Owner,
            input.Remark
        );
    }

    [Authorize(policy: CodeManagementPermissions.CodeManagement.Project.Delete)]
    public Task DeleteAsync(DeleteProjectInput input)
    {
        return _projectManager.DeleteAsync(input.Id);
    }

    public async Task<GetProjectAndEntityOutput> GetProjectAndEntityAsync(
        GetProjectAndEntityInput input
    )
    {
        var result = new GetProjectAndEntityOutput();
        var project = await _projectManager.GetAsync(input.Id);
        result.Project = new ProjectOutput()
        {
            Id = project.Id,
            CompanyName = project.CompanyName,
            ProjectName = project.ProjectName,
        };

        var entities = await _entityModelManager.FindByProjectIdAsync(input.Id);

        foreach (
            var entity in entities.Where(e => (e.ParentId != null || e.ParentId != Guid.Empty))
        )
        {
            result.Entities.Add(
                new EntityOutput()
                {
                    Id = entity.Id,
                    Code = entity.Code,
                    Description = entity.Description,
                }
            );
        }

        return result;
    }
}
