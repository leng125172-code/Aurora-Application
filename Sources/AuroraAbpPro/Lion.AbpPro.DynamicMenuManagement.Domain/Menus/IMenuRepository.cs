using Volo.Abp.Domain.Repositories;

namespace Lion.AbpPro.DynamicMenuManagement.Menus;

public interface IMenuRepository : IBasicRepository<Menu, Guid>
{
    Task<List<Menu>> GetListAsync(
        DateTime? startDateTime = null,
        DateTime? endDateTime = null,
        int maxResultCount = 10,
        int skipCount = 0
    );

    Task<long> GetCountAsync(DateTime? startDateTime = null, DateTime? endDateTime = null);

    /// <summary>
    /// 通过name获取菜单
    /// </summary>
    Task<Menu> ExistByNameAsync(string name);

    /// <summary>
    /// 通过path获取菜单
    /// </summary>
    Task<Menu> ExistByPathAsync(string path);

    /// <summary>
    /// 通过name获取菜单
    /// </summary>
    Task<Menu> HasByNameAsync(string name, Guid excludeId);

    /// <summary>
    /// 通过path获取菜单
    /// </summary>
    Task<Menu> HasByPathAsync(string path, Guid excludeId);

    Task<List<Menu>> FindByParentIdAsync(Guid? parentId);

    Task<Menu> FindByNameAsync(string name);
}
