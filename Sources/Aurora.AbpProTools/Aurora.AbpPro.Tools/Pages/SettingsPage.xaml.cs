using System;
using System.Collections.Specialized;
using System.Windows.Controls;
using Aurora.AbpPro.Tools.ViewModels;

namespace Aurora.AbpPro.Tools.Pages;

/// <summary>
/// 配置页：以 DataGrid 形式展示运行时日志，支持时间/级别/关键字筛选
/// </summary>
public partial class SettingsPage : UserControl
{
    private readonly SettingsPageViewModel _viewModel;

    /// <summary>
    /// 用于阻止 PasswordBox 初始化赋值时再次触发 PasswordChanged 引发的循环
    /// </summary>
    private bool _suppressPasswordChanged;

    public SettingsPage(SettingsPageViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        // 监听新条目，自动滚动到最后一行
        _viewModel.Entries.CollectionChanged += OnEntriesChanged;
        Unloaded += (_, _) =>
        {
            // VM 为单例，仅取消页面级订阅
            _viewModel.Entries.CollectionChanged -= OnEntriesChanged;
        };

        Loaded += OnLoadedHandler;
    }

    /// <summary>
    /// 页面加载完成：初始化 PasswordBox 显示并滚动到末尾
    /// </summary>
    private void OnLoadedHandler(object sender, System.Windows.RoutedEventArgs e)
    {
        // 同步 VM 中已存在的 Token 到 PasswordBox（避免与 PasswordChanged 形成回环）
        _suppressPasswordChanged = true;
        try
        {
            GitHubTokenBox.Password = _viewModel.GitHubToken ?? string.Empty;
        }
        finally
        {
            _suppressPasswordChanged = false;
        }
        ScrollToLast();
    }

    /// <summary>
    /// PasswordBox 内容变化：写回 ViewModel（VM 自动 debounce 持久化）
    /// </summary>
    private void GitHubTokenBox_OnPasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_suppressPasswordChanged)
        {
            return;
        }
        _viewModel.GitHubToken = GitHubTokenBox.Password;
    }

    /// <summary>
    /// 集合变化（新增）时滚动到最后一条
    /// </summary>
    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            Dispatcher.BeginInvoke(new System.Action(ScrollToLast));
        }
    }

    /// <summary>
    /// 滚动到 DataGrid 的最后一行
    /// </summary>
    private void ScrollToLast()
    {
        if (LogGrid.Items.Count == 0)
        {
            return;
        }
        var last = LogGrid.Items[LogGrid.Items.Count - 1];
        if (last is not null)
        {
            LogGrid.ScrollIntoView(last);
        }
    }
}
