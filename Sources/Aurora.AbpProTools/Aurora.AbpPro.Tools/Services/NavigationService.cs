using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using Aurora.AbpPro.Tools.Models;
using Aurora.AbpPro.Tools.Pages;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aurora.AbpPro.Tools.Services;

/// <summary>
/// 页面导航服务接口：基于菜单路由切换主区域内容
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// 菜单列表（绑定到侧边栏）
    /// </summary>
    IReadOnlyList<MenuItemModel> Menus { get; }

    /// <summary>
    /// 当前显示的页面
    /// </summary>
    UserControl? CurrentPage { get; }

    /// <summary>
    /// 当前页面变更事件
    /// </summary>
    event EventHandler? CurrentPageChanged;

    /// <summary>
    /// 通过菜单项导航
    /// </summary>
    void NavigateTo(MenuItemModel menu);

    /// <summary>
    /// 通过页面类型导航
    /// </summary>
    void NavigateTo<TPage>()
        where TPage : UserControl;
}

/// <summary>
/// 导航服务实现：基于 DI 解析页面实例并切换 ContentControl
/// </summary>
public class NavigationService : ObservableObject, INavigationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NavigationService> _logger;
    private UserControl? _currentPage;

    public NavigationService(IServiceProvider serviceProvider, ILogger<NavigationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        // 静态注册菜单与页面的映射关系
        Menus = new List<MenuItemModel>
        {
            new()
            {
                Key = "Lang.Menu.Home",
                IconKey = "HomeGeometry",
                PageType = typeof(HomePage),
            },
            new()
            {
                Key = "Lang.Menu.Framework",
                IconKey = "AppsGeometry",
                PageType = typeof(FrameworkPage),
            },
            new()
            {
                Key = "Lang.Menu.Project",
                IconKey = "ProjectGeometry",
                PageType = typeof(ProjectPage),
            },
            new()
            {
                Key = "Lang.Menu.Settings",
                IconKey = "ConfigGeometry",
                PageType = typeof(SettingsPage),
            },
        };
    }

    public IReadOnlyList<MenuItemModel> Menus { get; }

    public UserControl? CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public event EventHandler? CurrentPageChanged;

    public void NavigateTo(MenuItemModel menu)
    {
        if (menu?.PageType is null)
        {
            return;
        }
        NavigateToCore(menu.PageType);
    }

    public void NavigateTo<TPage>()
        where TPage : UserControl
    {
        NavigateToCore(typeof(TPage));
    }

    /// <summary>
    /// 实际执行页面切换：通过 DI 解析新页面，记录日志，触发事件
    /// </summary>
    private void NavigateToCore(Type pageType)
    {
        try
        {
            var page = (UserControl)_serviceProvider.GetRequiredService(pageType);
            CurrentPage = page;
            CurrentPageChanged?.Invoke(this, EventArgs.Empty);
            _logger.LogInformation("已导航到页面：{PageType}", pageType.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导航到页面 {PageType} 失败", pageType.Name);
        }
    }
}
