using Microsoft.Extensions.Localization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization.Permissions;

namespace Lion.AbpPro.DynamicMenuManagement.Menus;

/// <summary>
/// 菜单
/// </summary>
[Authorize]
public class MenuAppService : ApplicationService, IMenuAppService
{
    private readonly MenuManager _menuManager;
    private readonly IStringLocalizer<DynamicMenuManagementResource> _stringLocalizer;
    private readonly IPermissionChecker _permissionChecker;

    public MenuAppService(
        MenuManager menuManager,
        IStringLocalizer<DynamicMenuManagementResource> stringLocalizer,
        IPermissionChecker permissionChecker
    )
    {
        _menuManager = menuManager;
        _stringLocalizer = stringLocalizer;
        _permissionChecker = permissionChecker;
    }

    [Authorize]
    public async Task<List<GetMenuTreeOutput>> GetUserMenuAsync()
    {
        // todo 这里可以添加缓存
        var list = await _menuManager.GetListAsync(maxResultCount: Int32.MaxValue);
        // 判断权限
        var permissionGrantResult = await _permissionChecker.IsGrantedAsync(
            list.Where(e => e.Policy.IsNotNullOrWhiteSpace())
                .Select(e => e.Policy)
                .Distinct()
                .ToArray()
        );
        var allowMenus = new List<MenuDto>();
        foreach (var menu in list)
        {
            if (!menu.Enabled)
                continue;

            if (menu.Policy.IsNullOrWhiteSpace())
            {
                allowMenus.Add(menu);
            }
            else if (permissionGrantResult.Result[menu.Policy] == PermissionGrantResult.Granted)
            {
                allowMenus.Add(menu);
            }
        }

        // 递归list，获取树结构
        return BuildMenuTree(allowMenus, true);
    }

    /// <summary>
    /// 分页查询菜单
    /// </summary>
    public async Task<PagedResultDto<PageMenuOutput>> PageAsync(PageMenuInput input)
    {
        var result = new PagedResultDto<PageMenuOutput>();

        var list = await _menuManager.GetListAsync(
            input.StartCreationTime,
            input.EndCreationTime,
            input.PageSize,
            input.SkipCount
        );

        var items = new List<PageMenuOutput>();
        foreach (var item in list)
        {
            items.Add(
                new PageMenuOutput()
                {
                    Id = item.Id,
                    ParentId = item.ParentId,
                    Name = item.Name,
                    Title = item.Title,
                    DisplayTitle = item.DisplayTitle,
                    LocalizationTitle = item.DisplayTitle.IsNullOrWhiteSpace()
                        ? item.Title
                        : _stringLocalizer[item.DisplayTitle],
                    Icon = item.Icon,
                    KeepAlive = item.KeepAlive,
                    HideInMenu = item.HideInMenu,
                    Order = item.Order,
                    Path = item.Path,
                    MenuType = item.MenuType,
                    OpenType = item.OpenType,
                    Url = item.Url,
                    Component = item.Component,
                    Enabled = item.Enabled,
                    Policy = item.Policy,
                }
            );
        }

        result.Items = items;
        return result;
    }

    /// <summary>
    /// 创建菜单
    /// </summary>
    [Authorize(DynamicMenuManagementPermissions.DynamicMenuManagement.Create)]
    public Task CreateAsync(CreateMenuInput input)
    {
        return _menuManager.CreateAsync(
            GuidGenerator.Create(),
            input.ParentId,
            input.Name,
            input.Title,
            input.DisplayTitle,
            input.Icon,
            input.KeepAlive,
            input.HideInMenu,
            input.Order,
            input.Path,
            input.MenuType,
            input.OpenType,
            input.Url,
            input.Component,
            input.Enabled,
            input.Policy
        );
    }

    /// <summary>
    /// 编辑菜单
    /// </summary>
    [Authorize(DynamicMenuManagementPermissions.DynamicMenuManagement.Update)]
    public Task UpdateAsync(UpdateMenuInput input)
    {
        return _menuManager.UpdateAsync(
            input.Id,
            input.Name,
            input.Title,
            input.DisplayTitle,
            input.Icon,
            input.KeepAlive,
            input.HideInMenu,
            input.Order,
            input.Path,
            input.MenuType,
            input.OpenType,
            input.Url,
            input.Component,
            input.Enabled,
            input.Policy,
            input.ParentId
        );
    }

    /// <summary>
    /// 删除菜单
    /// </summary>
    [Authorize(DynamicMenuManagementPermissions.DynamicMenuManagement.Delete)]
    public Task DeleteAsync(DeleteMenuInput input)
    {
        return _menuManager.DeleteAsync(input.Id);
    }

    [Authorize]
    public async Task<List<GetMenuTreeOutput>> GetMenuTreeAsync()
    {
        var list = await _menuManager.GetListAsync(maxResultCount: Int32.MaxValue);
        // 递归list，获取树结构
        return BuildMenuTree(list);
    }

    private List<GetMenuTreeOutput> BuildMenuTree(List<MenuDto> menus, bool rootHasChildren = false)
    {
        // 1. 先筛选出所有根节点（ParentId为null）
        var roots = menus
            .Where(x => x.Enabled)
            .Where(x => x.ParentId == null)
            .OrderBy(x => x.Order) // 按Order字段排序
            .ToList();

        // 2. 创建字典用于快速查找子节点
        var childrenLookup = menus.Where(x => x.ParentId != null).ToLookup(x => x.ParentId!.Value); // 非null的ParentId作为Key

        // 3. 递归构建树形结构
        return roots
            .Select(rootDto => MapToTreeOutput(rootDto, childrenLookup))
            .WhereIf(rootHasChildren, treeNode => treeNode.Children.Any())
            .ToList();
    }

    private GetMenuTreeOutput MapToTreeOutput(MenuDto dto, ILookup<Guid, MenuDto> childrenLookup)
    {
        var output = new GetMenuTreeOutput
        {
            Id = dto.Id,
            ParentId = dto.ParentId,
            Name = dto.Name,
            Path = dto.Path,
            Component = dto.Component,
            Enabled = dto.Enabled,
            Meta = new GetMenuTreeMetaOutput()
            {
                Title = dto.DisplayTitle.IsNullOrWhiteSpace()
                    ? dto.Title
                    : _stringLocalizer[dto.DisplayTitle],
                DisplayTitle = dto.DisplayTitle.IsNullOrWhiteSpace()
                    ? dto.Title
                    : _stringLocalizer[dto.DisplayTitle],
                Icon = dto.Icon,
                KeepAlive = dto.KeepAlive,
                HideInMenu = dto.HideInMenu,
                Order = dto.Order,
                Link = dto.OpenType == OpenType.ExternalLink ? dto.Url : string.Empty,
                IframeSrc = dto.OpenType == OpenType.InternalLink ? dto.Url : string.Empty,
            },
        };

        // 递归处理子节点
        var children = childrenLookup[dto.Id]
            .OrderBy(x => x.Order) // 子节点按Order排序
            .Select(childDto => MapToTreeOutput(childDto, childrenLookup))
            .ToList();

        output.Children = children;
        return output;
    }
}
