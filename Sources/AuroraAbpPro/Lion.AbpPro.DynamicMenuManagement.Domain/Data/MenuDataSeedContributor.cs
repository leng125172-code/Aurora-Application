using Lion.AbpPro.DynamicMenuManagement.Menus;

namespace Lion.AbpPro.DynamicMenuManagement.Data;

public class MenuDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IMenuRepository _menuRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly IGuidGenerator _guidGenerator;

    public MenuDataSeedContributor(
        IMenuRepository menuRepository,
        ICurrentTenant currentTenant,
        IGuidGenerator guidGenerator
    )
    {
        _menuRepository = menuRepository;
        _currentTenant = currentTenant;
        _guidGenerator = guidGenerator;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        var menus = new List<Menu>();

        #region Dashboard 页面种子数据

        var dashboardId = _guidGenerator.Create();
        menus.Add(
            new Menu(
                dashboardId,
                null,
                MenuDataSeedConst.Dashboard.Name,
                MenuDataSeedConst.Dashboard.Meta.Title,
                MenuDataSeedConst.Dashboard.Meta.DisplayTitle,
                MenuDataSeedConst.Dashboard.Meta.Icon,
                false,
                false,
                MenuDataSeedConst.Dashboard.Meta.Order,
                MenuDataSeedConst.Dashboard.Path,
                MenuType.Folder,
                OpenType.Component,
                null,
                null,
                true,
                MenuDataSeedConst.Dashboard.Meta.Policy,
                _currentTenant.Id
            )
        );

        foreach (var item in MenuDataSeedConst.Dashboard.Children)
        {
            menus.Add(
                new Menu(
                    _guidGenerator.Create(),
                    dashboardId,
                    item.Name,
                    item.Meta.Title,
                    item.Meta.DisplayTitle,
                    item.Meta.Icon,
                    false,
                    false,
                    item.Meta.Order,
                    item.Path,
                    MenuType.Menu,
                    OpenType.Component,
                    null,
                    item.Component,
                    true,
                    item.Meta.Policy,
                    _currentTenant.Id
                )
            );
        }

        #endregion


        #region System 页面种子数据

        var systemId = _guidGenerator.Create();
        menus.Add(
            new Menu(
                systemId,
                null,
                MenuDataSeedConst.System.Name,
                MenuDataSeedConst.System.Meta.Title,
                MenuDataSeedConst.System.Meta.DisplayTitle,
                MenuDataSeedConst.System.Meta.Icon,
                false,
                false,
                MenuDataSeedConst.System.Meta.Order,
                MenuDataSeedConst.System.Path,
                MenuType.Folder,
                OpenType.Component,
                null,
                null,
                true,
                MenuDataSeedConst.System.Meta.Policy,
                _currentTenant.Id
            )
        );

        foreach (var item in MenuDataSeedConst.System.Children)
        {
            menus.Add(
                new Menu(
                    _guidGenerator.Create(),
                    systemId,
                    item.Name,
                    item.Meta.Title,
                    item.Meta.DisplayTitle,
                    item.Meta.Icon,
                    false,
                    false,
                    item.Meta.Order,
                    item.Path,
                    MenuType.Menu,
                    OpenType.Component,
                    null,
                    item.Component,
                    true,
                    item.Meta.Policy,
                    _currentTenant.Id
                )
            );
        }

        #endregion

        #region tenant 页面种子数据

        var tenantId = _guidGenerator.Create();
        menus.Add(
            new Menu(
                tenantId,
                null,
                MenuDataSeedConst.Tenant.Name,
                MenuDataSeedConst.Tenant.Meta.Title,
                MenuDataSeedConst.Tenant.Meta.DisplayTitle,
                MenuDataSeedConst.Tenant.Meta.Icon,
                false,
                false,
                MenuDataSeedConst.Tenant.Meta.Order,
                MenuDataSeedConst.Tenant.Path,
                MenuType.Folder,
                OpenType.Component,
                null,
                null,
                true,
                MenuDataSeedConst.Tenant.Meta.Policy,
                _currentTenant.Id
            )
        );

        foreach (var item in MenuDataSeedConst.Tenant.Children)
        {
            menus.Add(
                new Menu(
                    _guidGenerator.Create(),
                    tenantId,
                    item.Name,
                    item.Meta.Title,
                    item.Meta.DisplayTitle,
                    item.Meta.Icon,
                    false,
                    false,
                    item.Meta.Order,
                    item.Path,
                    MenuType.Menu,
                    OpenType.Component,
                    null,
                    item.Component,
                    true,
                    item.Meta.Policy,
                    _currentTenant.Id
                )
            );
        }

        #endregion

        #region file 页面种子数据

        var fileId = _guidGenerator.Create();
        menus.Add(
            new Menu(
                fileId,
                null,
                MenuDataSeedConst.File.Name,
                MenuDataSeedConst.File.Meta.Title,
                MenuDataSeedConst.File.Meta.DisplayTitle,
                MenuDataSeedConst.File.Meta.Icon,
                false,
                false,
                MenuDataSeedConst.File.Meta.Order,
                MenuDataSeedConst.File.Path,
                MenuType.Folder,
                OpenType.Component,
                null,
                null,
                true,
                MenuDataSeedConst.File.Meta.Policy,
                _currentTenant.Id
            )
        );

        foreach (var item in MenuDataSeedConst.File.Children)
        {
            menus.Add(
                new Menu(
                    _guidGenerator.Create(),
                    fileId,
                    item.Name,
                    item.Meta.Title,
                    item.Meta.DisplayTitle,
                    item.Meta.Icon,
                    false,
                    false,
                    item.Meta.Order,
                    item.Path,
                    MenuType.Menu,
                    OpenType.Component,
                    null,
                    item.Component,
                    true,
                    item.Meta.Policy,
                    _currentTenant.Id
                )
            );
        }

        #endregion

        foreach (var menu in menus)
        {
            var entity = await _menuRepository.FindByNameAsync(menu.Name);

            if (entity == null)
            {
                await _menuRepository.InsertAsync(menu);
            }
        }
    }
}
