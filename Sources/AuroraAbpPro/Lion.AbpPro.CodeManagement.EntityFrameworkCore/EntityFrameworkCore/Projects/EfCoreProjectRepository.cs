using Lion.AbpPro.CodeManagement.Projects;
using Lion.AbpPro.CodeManagement.Projects.Aggregates;

namespace Lion.AbpPro.CodeManagement.EntityFrameworkCore.Projects;

public class EfCoreProjectRepository
    : EfCoreRepository<ICodeManagementDbContext, Project, Guid>,
        IProjectRepository
{
    public EfCoreProjectRepository(IDbContextProvider<ICodeManagementDbContext> dbContextProvider)
        : base(dbContextProvider) { }

    public async Task<Project> FindAsync(string projectName)
    {
        return await (await GetDbSetAsync()).FirstOrDefaultAsync(t => t.ProjectName == projectName);
    }

    public async Task<Project> FindByNameExcludeIdAsync(
        string projectName,
        Guid id,
        bool includeDetail = true
    )
    {
        return await (await GetDbSetAsync()).FirstOrDefaultAsync(t =>
            t.ProjectName == projectName && t.Id != id
        );
    }

    public async Task<List<Project>> GetListAsync(
        string filter = null,
        int maxResultCount = 10,
        int skipCount = 0,
        bool includeDetails = true
    )
    {
        return await (await GetDbSetAsync())
            .WhereIf(!filter.IsNullOrWhiteSpace(), e => (e.ProjectName.Contains(filter)))
            .OrderByDescending(e => e.CreationTime)
            .PageBy(skipCount, maxResultCount)
            .ToListAsync();
    }

    public async Task<long> GetCountAsync(string filter = null)
    {
        return await (await GetDbSetAsync())
            .WhereIf(!filter.IsNullOrWhiteSpace(), e => (e.ProjectName.Contains(filter)))
            .CountAsync();
    }
}
