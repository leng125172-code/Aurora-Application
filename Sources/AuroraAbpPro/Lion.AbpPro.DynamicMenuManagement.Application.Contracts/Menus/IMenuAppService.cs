using Lion.AbpPro.DynamicMenuManagement.Menus;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Lion.AbpPro.DynamicMenuManagement.Menus;

/// <summary>
/// 菜单
/// </summary>
public interface IMenuAppService : IApplicationService
{
    Task<List<GetMenuTreeOutput>> GetUserMenuAsync();

    /// <summary>
    /// 分页查询菜单
    /// </summary>
    Task<PagedResultDto<PageMenuOutput>> PageAsync(PageMenuInput input);

    /// <summary>
    /// 创建菜单
    /// </summary>
    Task CreateAsync(CreateMenuInput input);

    /// <summary>
    /// 编辑菜单
    /// </summary>
    Task UpdateAsync(UpdateMenuInput input);

    /// <summary>
    /// 删除菜单
    /// </summary>
    Task DeleteAsync(DeleteMenuInput input);

    /// <summary>
    /// 获取菜单树
    /// </summary>
    Task<List<GetMenuTreeOutput>> GetMenuTreeAsync();
}
