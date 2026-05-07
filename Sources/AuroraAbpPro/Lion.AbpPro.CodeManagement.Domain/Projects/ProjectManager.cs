namespace Lion.AbpPro.CodeManagement.Projects;

public class ProjectManager : CodeManagementDomainService
{
    private readonly IProjectRepository _projectRepository;

    public ProjectManager(IProjectRepository projectRepository)
    {
        _projectRepository = projectRepository;
    }

    public async Task<List<ProjectDto>> GetListAsync(
        string filter = null,
        int maxResultCount = 10,
        int skipCount = 0,
        bool includeDetails = true
    )
    {
        var list = await _projectRepository.GetListAsync(
            filter,
            maxResultCount,
            skipCount,
            includeDetails
        );
        return list.Adapt<List<ProjectDto>>();
    }

    public async Task<long> GetCountAsync(string filter = null)
    {
        return await _projectRepository.GetCountAsync(filter);
    }

    public async Task<ProjectDto> CreateAsync(
        string companyName,
        string projectName,
        bool supportTenant,
        string owner = null,
        string remark = null
    )
    {
        if (companyName.IsNullOrWhiteSpace())
            throw new UserFriendlyException("公司名称不能为空");
        if (projectName.IsNullOrWhiteSpace())
            throw new UserFriendlyException("项目名称不能为空");

        var entity = await _projectRepository.FindAsync(projectName);
        if (entity != null)
            throw new UserFriendlyException($"{projectName}项目已存在");
        entity = new Project(
            GuidGenerator.Create(),
            owner,
            companyName,
            projectName,
            supportTenant,
            remark,
            CurrentTenant.Id
        );

        await _projectRepository.InsertAsync(entity);
        return entity.Adapt<ProjectDto>();
    }

    public async Task<ProjectDto> UpdateAsync(
        Guid id,
        string companyName,
        string projectName,
        bool supportTenant,
        string owner = null,
        string remark = null
    )
    {
        if (companyName.IsNullOrWhiteSpace())
            throw new UserFriendlyException("公司名称不能为空");
        if (projectName.IsNullOrWhiteSpace())
            throw new UserFriendlyException("项目名称不能为空");
        var entity = await _projectRepository.FindAsync(id);
        if (entity == null)
            throw new UserFriendlyException($"项目不存在");
        var exist = await _projectRepository.FindByNameExcludeIdAsync(projectName, id);
        if (exist != null)
            throw new UserFriendlyException($"{projectName}项目已存在");
        entity.Update(companyName, projectName, owner, remark, supportTenant);
        await _projectRepository.UpdateAsync(entity);
        return entity.Adapt<ProjectDto>();
    }

    public async Task DeleteAsync(Guid id)
    {
        var entity = await _projectRepository.FindAsync(id);
        if (entity == null)
            throw new UserFriendlyException($"项目不存在");
        await _projectRepository.DeleteAsync(entity);
    }

    public async Task<ProjectDto> GetAsync(Guid id)
    {
        var entity = await _projectRepository.FindAsync(id);
        if (entity == null)
            throw new UserFriendlyException($"项目不存在");
        return entity.Adapt<ProjectDto>();
    }
}
