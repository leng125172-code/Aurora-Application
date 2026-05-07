namespace Lion.AbpPro.CodeManagement.Projects;

public interface IProjectRepository : IBasicRepository<Project, Guid>
{
    Task<Project> FindAsync(string projectName);

    /// <summary>
    /// 根据名称查询模板，但是排除指定id的模板
    /// </summary>
    Task<Project> FindByNameExcludeIdAsync(string projectName, Guid id, bool includeDetail = true);

    /// <summary>
    /// 分页获取项目
    /// </summary>
    Task<List<Project>> GetListAsync(
        string filter = null,
        int maxResultCount = 10,
        int skipCount = 0,
        bool includeDetails = true
    );

    Task<long> GetCountAsync(string filter = null);
}
