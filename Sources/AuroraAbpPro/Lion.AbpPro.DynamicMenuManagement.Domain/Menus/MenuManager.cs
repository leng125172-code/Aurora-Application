using Mapster;
using Volo.Abp;
using Volo.Abp.Domain.Services;
using Volo.Abp.ObjectMapping;

namespace Lion.AbpPro.DynamicMenuManagement.Menus;

public class MenuManager : DomainService
{
    private readonly IMenuRepository _menuRepository;
    private readonly IObjectMapper _objectMapper;
    private readonly ICurrentTenant _currentTenant;

    public MenuManager(
        IMenuRepository menuRepository,
        IObjectMapper objectMapper,
        ICurrentTenant currentTenant
    )
    {
        _menuRepository = menuRepository;
        _objectMapper = objectMapper;
        _currentTenant = currentTenant;
    }

    public async Task<List<MenuDto>> GetListAsync(
        DateTime? startDateTime = null,
        DateTime? endDateTime = null,
        int maxResultCount = 10,
        int skipCount = 0
    )
    {
        var list = await _menuRepository.GetListAsync(
            startDateTime,
            endDateTime,
            maxResultCount,
            skipCount
        );
        return list.Adapt<List<MenuDto>>();
    }

    public async Task<long> GetCountAsync(
        DateTime? startDateTime = null,
        DateTime? endDateTime = null
    )
    {
        return await _menuRepository.GetCountAsync(startDateTime, endDateTime);
    }

    /// <summary>
    /// 创建菜单
    /// </summary>
    public async Task<MenuDto> CreateAsync(
        Guid id,
        Guid? parentId,
        string name,
        string title,
        string displayTitle,
        string icon,
        bool keepAlive,
        bool hideInMenu,
        int order,
        string path,
        MenuType menuType,
        OpenType openType,
        string? url,
        string? component,
        bool enabled,
        string policy
    )
    {
        var entity = new Menu(
            id,
            parentId,
            name,
            title,
            displayTitle,
            icon,
            keepAlive,
            hideInMenu,
            order,
            path,
            menuType,
            openType,
            url,
            component,
            enabled,
            policy,
            _currentTenant.Id
        );
        await ExistByNameAsync(name);
        await ExistByPathAsync(path);
        entity = await _menuRepository.InsertAsync(entity);
        return entity.Adapt<MenuDto>();
    }

    /// <summary>
    /// 更新菜单
    /// </summary>
    public async Task<MenuDto> UpdateAsync(
        Guid id,
        string name,
        string title,
        string displayTitle,
        string icon,
        bool keepAlive,
        bool hideInMenu,
        int order,
        string path,
        MenuType menuType,
        OpenType openType,
        string? url,
        string? component,
        bool enabled,
        string policy,
        Guid? parentId
    )
    {
        var entity = await _menuRepository.FindAsync(id);
        if (entity == null)
            throw new MenuDomainException(DynamicMenuManagementErrorCodes.MenuNotExist);
        // 判断是否有name或者path相同
        await ExistByNameAsync(name, id);
        await ExistByPathAsync(path, id);
        entity.Update(
            name,
            title,
            displayTitle,
            icon,
            keepAlive,
            hideInMenu,
            order,
            path,
            menuType,
            openType,
            url,
            component,
            enabled,
            policy,
            parentId
        );
        entity = await _menuRepository.UpdateAsync(entity);
        return entity.Adapt<MenuDto>();
    }

    /// <summary>
    /// 删除菜单
    /// </summary>
    public async Task DeleteAsync(Guid id)
    {
        var entity = await _menuRepository.FindAsync(id);

        if (entity == null)
            throw new MenuDomainException(DynamicMenuManagementErrorCodes.MenuNotExist);
        // 同时删除子集所有的菜单
        var list = await _menuRepository.FindByParentIdAsync(id);
        foreach (var menu in list)
        {
            await DeleteAsync(menu.Id);
        }

        await _menuRepository.DeleteAsync(entity);
    }

    /// <summary>
    /// 通过name获取菜单
    /// </summary>
    public async Task ExistByNameAsync(string name)
    {
        var entity = await _menuRepository.ExistByNameAsync(name);
        if (entity != null)
            throw new MenuDomainException(DynamicMenuManagementErrorCodes.MenuExist);
    }

    /// <summary>
    /// 通过path获取菜单
    /// </summary>
    public async Task ExistByPathAsync(string path)
    {
        var entity = await _menuRepository.ExistByPathAsync(path);
        if (entity != null)
            throw new MenuDomainException(DynamicMenuManagementErrorCodes.MenuPathExist);
    }

    /// <summary>
    /// 通过name获取菜单
    /// </summary>
    public async Task ExistByNameAsync(string name, Guid excludeId)
    {
        var entity = await _menuRepository.HasByNameAsync(name, excludeId);
        if (entity != null)
            throw new MenuDomainException(DynamicMenuManagementErrorCodes.MenuExist);
    }

    /// <summary>
    /// 通过path获取菜单
    /// </summary>
    public async Task ExistByPathAsync(string path, Guid excludeId)
    {
        var entity = await _menuRepository.HasByPathAsync(path, excludeId);
        if (entity != null)
            throw new MenuDomainException(DynamicMenuManagementErrorCodes.MenuPathExist);
    }
}
