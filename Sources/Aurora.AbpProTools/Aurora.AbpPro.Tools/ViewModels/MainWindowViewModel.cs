using System.Collections.Generic;
using System.Collections.ObjectModel;
using Aurora.AbpPro.Tools.Models;
using Aurora.AbpPro.Tools.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aurora.AbpPro.Tools.ViewModels;

/// <summary>
/// 主窗口 ViewModel：承载菜单数据、当前页面、主题与语言切换命令
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private readonly IThemeService _themeService;
    private readonly ILocalizationService _localizationService;
    private readonly ILogger<MainWindowViewModel> _logger;

    /// <summary>
    /// 应用标题
    /// </summary>
    [ObservableProperty]
    private string _title;

    /// <summary>
    /// 版本号显示文本（取自当前程序集版本）
    /// </summary>
    public string Version { get; } =
        $"v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0"}";

    /// <summary>
    /// 当前选中的菜单
    /// </summary>
    [ObservableProperty]
    private MenuItemModel? _selectedMenu;

    /// <summary>
    /// 当前显示的页面（绑定到主区域 ContentControl）
    /// </summary>
    [ObservableProperty]
    private object? _currentPage;

    /// <summary>
    /// 可选语言列表
    /// </summary>
    public IReadOnlyList<string> Languages { get; } = new[] { "zh-CN", "en-US" };

    /// <summary>
    /// 可选主题列表（对应 HandyControl 的 SkinType 名称）
    /// </summary>
    public IReadOnlyList<string> Themes { get; } = new[] { "Default", "Dark", "Violet" };

    /// <summary>
    /// 菜单集合（绑定到侧边栏）
    /// </summary>
    public ObservableCollection<MenuItemModel> Menus { get; }

    public MainWindowViewModel(
        INavigationService navigationService,
        IThemeService themeService,
        ILocalizationService localizationService,
        IOptions<AppSettings> options,
        ILogger<MainWindowViewModel> logger
    )
    {
        _navigationService = navigationService;
        _themeService = themeService;
        _localizationService = localizationService;
        _logger = logger;
        _title = options.Value.Title;

        Menus = new ObservableCollection<MenuItemModel>(navigationService.Menus);
        RefreshMenuDisplayNames();

        // 监听语言变更，刷新菜单显示
        _localizationService.LanguageChanged += (_, _) =>
        {
            RefreshMenuDisplayNames();
            // 触发当前页面重建以刷新文本
            if (SelectedMenu is not null)
            {
                _navigationService.NavigateTo(SelectedMenu);
            }
        };

        // 监听导航变化，同步到 ViewModel
        _navigationService.CurrentPageChanged += (_, _) =>
            CurrentPage = _navigationService.CurrentPage;

        // 默认导航到首页
        SelectedMenu = Menus.Count > 0 ? Menus[0] : null;
    }

    /// <summary>
    /// 选中菜单变更时自动导航
    /// </summary>
    partial void OnSelectedMenuChanged(MenuItemModel? value)
    {
        if (value is null)
        {
            return;
        }
        _navigationService.NavigateTo(value);
    }

    /// <summary>
    /// 切换皮肤命令
    /// </summary>
    [RelayCommand]
    private void SwitchTheme(string skinName)
    {
        if (string.IsNullOrWhiteSpace(skinName))
        {
            return;
        }
        _logger.LogInformation("切换皮肤：{Skin}", skinName);
        _themeService.ApplySkin(skinName);
    }

    /// <summary>
    /// 切换语言命令
    /// </summary>
    [RelayCommand]
    private void SwitchLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return;
        }
        _logger.LogInformation("切换语言：{Language}", language);
        _localizationService.ApplyLanguage(language);
    }

    /// <summary>
    /// 刷新菜单显示名称（语言切换后调用）
    /// </summary>
    private void RefreshMenuDisplayNames()
    {
        foreach (var menu in Menus)
        {
            menu.DisplayName = _localizationService.GetText(menu.Key);
        }
        // 触发 ItemsControl 刷新
        var temp = SelectedMenu;
        OnPropertyChanged(nameof(Menus));
        SelectedMenu = temp;
    }
}
