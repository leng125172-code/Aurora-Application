using System;
using System.Globalization;
using System.Threading;
using System.Windows;

namespace Aurora.AbpPro.Tools.Services;

/// <summary>
/// 多语言切换事件参数
/// </summary>
public class LanguageChangedEventArgs : EventArgs
{
    public string Language { get; }

    public LanguageChangedEventArgs(string language) => Language = language;
}

/// <summary>
/// 本地化服务接口：动态切换语言资源字典
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// 当前语言代码
    /// </summary>
    string CurrentLanguage { get; }

    /// <summary>
    /// 语言变更事件
    /// </summary>
    event EventHandler<LanguageChangedEventArgs>? LanguageChanged;

    /// <summary>
    /// 应用指定语言
    /// </summary>
    void ApplyLanguage(string language);

    /// <summary>
    /// 根据资源键获取翻译文本
    /// </summary>
    string GetText(string key);
}

/// <summary>
/// 本地化服务实现：替换 App.xaml 第 2 个 ResourceDictionary 中的语言资源
/// </summary>
public class LocalizationService : ILocalizationService
{
    /// <summary>
    /// App.xaml 中语言资源在 MergedDictionaries 中的索引
    /// </summary>
    private const int LangResourceIndex = 2;

    private readonly IAppSettingsService _settingsService;

    public LocalizationService(IAppSettingsService settingsService)
    {
        _settingsService = settingsService;
        CurrentLanguage = settingsService.Current.Language;
    }

    public string CurrentLanguage { get; private set; }

    public event EventHandler<LanguageChangedEventArgs>? LanguageChanged;

    public void ApplyLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            language = "zh-CN";
        }

        var uri = new Uri(
            $"pack://application:,,,/Aurora.AbpPro.Tools;component/Resources/Langs/{language}.xaml",
            UriKind.Absolute
        );
        var dict = new ResourceDictionary { Source = uri };

        var merged = Application.Current.Resources.MergedDictionaries;
        if (merged.Count > LangResourceIndex)
        {
            merged[LangResourceIndex] = dict;
        }
        else
        {
            merged.Add(dict);
        }

        // 同步线程文化，便于格式化
        try
        {
            var culture = new CultureInfo(language);
            Thread.CurrentThread.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        catch
        {
            // 忽略不识别的文化代码
        }

        CurrentLanguage = language;
        _settingsService.Current.Language = language;
        _settingsService.Save();

        LanguageChanged?.Invoke(this, new LanguageChangedEventArgs(language));
    }

    public string GetText(string key)
    {
        var value = Application.Current.TryFindResource(key);
        return value?.ToString() ?? key;
    }
}
