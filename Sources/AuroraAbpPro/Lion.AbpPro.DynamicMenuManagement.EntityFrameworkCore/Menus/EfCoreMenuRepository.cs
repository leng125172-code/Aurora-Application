using Lion.AbpPro.DynamicMenuManagement.EntityFrameworkCore;

namespace Lion.AbpPro.DynamicMenuManagement.Menus;

/// <summary>
/// 菜单 仓储Ef core 实现
/// </summary>
public class EfCoreMenuRepository
    : EfCoreRepository<DynamicMenuManagementDbContext, Menu, Guid>,
        IMenuRepository
{
    public EfCoreMenuRepository(
        IDbContextProvider<DynamicMenuManagementDbContext> dbContextProvider
    )
        : base(dbContextProvider) { }

    public async Task<List<Menu>> GetListAsync(
        DateTime? startDateTime = null,
        DateTime? endDateTime = null,
        int maxResultCount = 10,
        int skipCount = 0
    )
    {
        return await (await GetDbSetAsync())
            .WhereIf(startDateTime.HasValue, e => e.CreationTime >= startDateTime.Value)
            .WhereIf(endDateTime.HasValue, e => e.CreationTime <= endDateTime.Value)
            .OrderBy(e => e.Order)
            .PageBy(skipCount, maxResultCount)
            .ToListAsync();
    }

    public async Task<long> GetCountAsync(
        DateTime? startDateTime = null,
        DateTime? endDateTime = null
    )
    {
        return await (await GetDbSetAsync())
            .WhereIf(startDateTime.HasValue, e => e.CreationTime >= startDateTime.Value)
            .WhereIf(endDateTime.HasValue, e => e.CreationTime <= endDateTime.Value)
            .CountAsync();
    }

    public async Task<Menu> ExistByNameAsync(string name)
    {
        if (name.IsNotNullOrWhiteSpace())
        {
            name = name.ToLower();
        }

        return await (await GetDbSetAsync()).FirstOrDefaultAsync(e => e.Name.ToLower() == name);
    }

    public async Task<Menu> ExistByPathAsync(string path)
    {
        if (path.IsNotNullOrWhiteSpace())
        {
            path = path.ToLower();
        }

        return await (await GetDbSetAsync()).FirstOrDefaultAsync(e => e.Path.ToLower() == path);
    }

    public async Task<Menu> HasByNameAsync(string name, Guid excludeId)
    {
        if (name.IsNotNullOrWhiteSpace())
        {
            name = name.ToLower();
        }

        return await (await GetDbSetAsync())
            .Where(e => e.Id != excludeId)
            .FirstOrDefaultAsync(e => e.Name.ToLower() == name);
    }

    public async Task<Menu> HasByPathAsync(string path, Guid excludeId)
    {
        if (path.IsNotNullOrWhiteSpace())
        {
            path = path.ToLower();
        }

        return await (await GetDbSetAsync())
            .Where(e => e.Id != excludeId)
            .FirstOrDefaultAsync(e => e.Path.ToLower() == path);
    }

    public async Task<List<Menu>> FindByParentIdAsync(Guid? parentId)
    {
        return await (await GetDbSetAsync()).Where(e => e.ParentId == parentId).ToListAsync();
    }

    public async Task<Menu> FindByNameAsync(string name)
    {
        return await (await GetDbSetAsync()).FirstOrDefaultAsync(e => e.Name == name);
    }
}
