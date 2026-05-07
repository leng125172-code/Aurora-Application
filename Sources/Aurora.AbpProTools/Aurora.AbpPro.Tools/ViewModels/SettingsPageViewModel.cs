using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using Aurora.AbpPro.Tools.Models;
using Aurora.AbpPro.Tools.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aurora.AbpPro.Tools.ViewModels;

/// <summary>
/// 配置页 ViewModel：以 DataGrid 形式展示运行时日志，支持
/// 时间范围筛选、级别筛选、关键字搜索。
/// 注册为单例（DI），日志缓冲在 IUiLogSink 中维护，跨页面切换不丢失。
/// </summary>
public partial class SettingsPageViewModel : ObservableObject
{
    /// <summary>
    /// 表示"全部级别"的特殊选项值
    /// </summary>
    public const string AllLevels = "All";

    /// <summary>
    /// UI 侧保留的最大日志条数（与 Sink 一致）
    /// </summary>
    private const int MaxEntryCount = 1_200;

    private readonly IUiLogSink _logSink;
    private readonly IAppSettingsService _settingsService;

    /// <summary>
    /// 保存节流定时器：避免每次按键都写盘
    /// </summary>
    private readonly DispatcherTimer _saveTimer;

    /// <summary>
    /// 原始日志集合（按写入顺序追加）
    /// </summary>
    public ObservableCollection<LogEntry> Entries { get; } = new();

    /// <summary>
    /// 经过筛选后的视图（绑定到 DataGrid）
    /// </summary>
    public ICollectionView EntriesView { get; }

    /// <summary>
    /// 可选的级别集合（含 "All"）
    /// </summary>
    public IReadOnlyList<string> LevelOptions { get; } =
        new[] { AllLevels, "Trace", "Debug", "Info", "Warn", "Error", "Fatal" };

    /// <summary>
    /// 起始时间筛选（含）
    /// </summary>
    [ObservableProperty]
    private DateTime? _fromTime;

    /// <summary>
    /// 结束时间筛选（含）
    /// </summary>
    [ObservableProperty]
    private DateTime? _toTime;

    /// <summary>
    /// 当前选中的级别筛选项
    /// </summary>
    [ObservableProperty]
    private string _selectedLevel = AllLevels;

    /// <summary>
    /// 关键字搜索（匹配 Logger 或 Message）
    /// </summary>
    [ObservableProperty]
    private string _keyword = string.Empty;

    /// <summary>
    /// GitHub 个人访问令牌（PAT）；变化后自动持久化
    /// </summary>
    [ObservableProperty]
    private string _gitHubToken = string.Empty;

    public SettingsPageViewModel(IUiLogSink logSink, IAppSettingsService settingsService)
    {
        _logSink = logSink;
        _settingsService = settingsService;

        // 从配置服务还原 Token
        _gitHubToken = _settingsService.Current.GitHubToken ?? string.Empty;

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += OnSaveTimerTick;

        // 还原历史快照
        foreach (var entry in _logSink.Snapshot)
        {
            Entries.Add(entry);
        }

        EntriesView = CollectionViewSource.GetDefaultView(Entries);
        EntriesView.Filter = FilterEntry;

        _logSink.EntryAdded += OnEntryAdded;
        _logSink.Cleared += OnCleared;
    }

    /// <summary>
    /// 收到新日志时追加到集合；超出上限时从头部移除最旧一条
    /// </summary>
    private void OnEntryAdded(object? sender, LogEntry entry)
    {
        Entries.Add(entry);
        while (Entries.Count > MaxEntryCount)
        {
            Entries.RemoveAt(0);
        }
    }

    /// <summary>
    /// 接收清空事件，清理本地集合
    /// </summary>
    private void OnCleared(object? sender, EventArgs e)
    {
        Entries.Clear();
    }

    /// <summary>
    /// CollectionView 过滤器：按时间区间、级别与关键字筛选
    /// </summary>
    private bool FilterEntry(object obj)
    {
        if (obj is not LogEntry entry)
        {
            return false;
        }

        if (FromTime.HasValue && entry.Timestamp < FromTime.Value)
        {
            return false;
        }

        if (ToTime.HasValue && entry.Timestamp > ToTime.Value)
        {
            return false;
        }

        if (
            !string.IsNullOrEmpty(SelectedLevel)
            && !string.Equals(SelectedLevel, AllLevels, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(entry.Level, SelectedLevel, StringComparison.OrdinalIgnoreCase)
        )
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(Keyword))
        {
            var key = Keyword.Trim();
            if (
                (entry.Message?.IndexOf(key, StringComparison.OrdinalIgnoreCase) ?? -1) < 0
                && (entry.Logger?.IndexOf(key, StringComparison.OrdinalIgnoreCase) ?? -1) < 0
            )
            {
                return false;
            }
        }

        return true;
    }

    partial void OnFromTimeChanged(DateTime? value) => EntriesView?.Refresh();

    partial void OnToTimeChanged(DateTime? value) => EntriesView?.Refresh();

    partial void OnSelectedLevelChanged(string value) => EntriesView?.Refresh();

    partial void OnKeywordChanged(string value) => EntriesView?.Refresh();

    /// <summary>
    /// GitHub Token 变更：重置 debounce 定时器，到期后一次性保存
    /// </summary>
    partial void OnGitHubTokenChanged(string value)
    {
        if (_settingsService is null)
        {
            return;
        }
        _settingsService.Current.GitHubToken = value ?? string.Empty;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>
    /// 节流定时器到期：将当前设置写入磁盘
    /// </summary>
    private void OnSaveTimerTick(object? sender, EventArgs e)
    {
        _saveTimer.Stop();
        _settingsService.Save();
    }

    /// <summary>
    /// 重置所有筛选条件
    /// </summary>
    [RelayCommand]
    private void ResetFilter()
    {
        FromTime = null;
        ToTime = null;
        SelectedLevel = AllLevels;
        Keyword = string.Empty;
    }
}
