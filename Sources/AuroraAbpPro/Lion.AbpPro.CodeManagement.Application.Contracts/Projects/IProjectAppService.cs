namespace Lion.AbpPro.CodeManagement.Projects;

public interface IProjectAppService : IApplicationService
{
    Task<List<ProjectDto>> AllAsync();

    Task<PagedResultDto<ProjectDto>> PageAsync(PageProjectInput input);

    Task CreateAsync(CreateProjectInput input);

    Task UpdateAsync(UpdateProjectInput input);

    Task DeleteAsync(DeleteProjectInput input);

    /// <summary>
    /// 获取项目和实体信息
    /// </summary>
    Task<GetProjectAndEntityOutput> GetProjectAndEntityAsync(GetProjectAndEntityInput input);
}
