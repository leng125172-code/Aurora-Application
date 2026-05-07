using System.Windows;
using HandyControl.Data;
using HandyControl.Tools;

namespace Aurora.AbpPro.Tools.Services;

/// <summary>
/// 主题服务接口：动态切换 HandyControl 皮肤
/// </summary>
public interface IThemeService
{
    /// <summary>
    /// 当前皮肤名称
    /// </summary>
    string CurrentSkin { get; }

    /// <summary>
    /// 应用指定皮肤
    /// </summary>
    void ApplySkin(string skinName);
}

/// <summary>
/// 主题服务实现：替换 App.xaml 第 0 个 ResourceDictionary 中的皮肤资源
/// </summary>
public class ThemeService : IThemeService
{
    private readonly IAppSettingsService _settingsService;

    public ThemeService(IAppSettingsService settingsService)
    {
        _settingsService = settingsService;
        CurrentSkin = settingsService.Current.Skin;
    }

    public string CurrentSkin { get; private set; }

    public void ApplySkin(string skinName)
    {
        if (!Enum.TryParse<SkinType>(skinName, ignoreCase: true, out var skin))
        {
            skin = SkinType.Default;
        }

        var resources = Application.Current.Resources;

        // [0] 皮肤外壳：清空内部字典，写入新的皮肤颜色资源
        var skinShell = resources.MergedDictionaries[0];
        skinShell.MergedDictionaries.Clear();
        skinShell.MergedDictionaries.Add(ResourceHelper.GetSkin(skin));

        // [1] 主题外壳：必须重新加载 HandyControl Theme.xaml，
        //              让其中通过 StaticResource 引用颜色的 Brush 重新构造，
        //              否则界面颜色不会随皮肤变化。
        var themeShell = resources.MergedDictionaries[1];
        themeShell.MergedDictionaries.Clear();
        themeShell.MergedDictionaries.Add(
            new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/HandyControl;component/Themes/Theme.xaml"),
            }
        );

        // 通知主窗口重新应用模板，确保 hc:Window 等控件刷新
        Application.Current.MainWindow?.OnApplyTemplate();

        CurrentSkin = skin.ToString();
        _settingsService.Current.Skin = CurrentSkin;
        _settingsService.Save();
    }
}
