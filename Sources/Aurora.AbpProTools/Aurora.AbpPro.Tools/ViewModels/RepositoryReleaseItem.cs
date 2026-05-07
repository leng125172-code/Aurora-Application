using System.Collections.ObjectModel;
using System.Threading;
using Aurora.AbpPro.Tools.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Octokit;

namespace Aurora.AbpPro.Tools.ViewModels;

/// <summary>
/// 仓库 Release 卡片项：承载单个仓库的展示信息、加载状态与下载进度
/// </summary>
public partial class RepositoryReleaseItem : ObservableObject
{
    /// <summary>
    /// 关联的仓库配置（用于后续发起下载等操作）
    /// </summary>
    public RepositoryConfig Repository { get; }

    /// <summary>
    /// Owner / RepositoryId 拼接的显示标题
    /// </summary>
    public string Title => $"{Repository.Owner}/{Repository.RepositoryId}";

    /// <summary>
    /// 仓库类型显示文本
    /// </summary>
    public string TypeText =>
        Repository.RepositoryType == Models.RepositoryType.Business ? "商业版本" : "开源版本";

    /// <summary>
    /// 仓库描述
    /// </summary>
    public string Description => Repository.Description;

    /// <summary>
    /// 是否正在加载 Release 信息
    /// </summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// 加载错误信息（为空表示无错误）
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// 最新 Release 标签
    /// </summary>
    [ObservableProperty]
    private string _latestTag = string.Empty;

    /// <summary>
    /// 最新 Release 名称
    /// </summary>
    [ObservableProperty]
    private string _latestName = string.Empty;

    /// <summary>
    /// 最新 Release 发布时间（已格式化）
    /// </summary>
    [ObservableProperty]
    private string _publishedAt = string.Empty;

    /// <summary>
    /// 实际使用版本：执行「下载框架」时按依赖链解析得到、真正下载的版本号。
    /// 与 <see cref="LatestTag"/> 不一定相同（例如 abpframework/abp 由 abp-vnext-pro *.targets 决定）
    /// </summary>
    [ObservableProperty]
    private string _usedVersion = string.Empty;

    /// <summary>
    /// 最近若干个 Release（用于卡片底部下拉/列表显示）
    /// </summary>
    public ObservableCollection<ReleaseSummary> RecentReleases { get; } = new();

    /// <summary>
    /// 缓存最新 Release 对象（下载时使用，避免再次请求 API）
    /// </summary>
    public Release? LatestRelease { get; set; }

    /// <summary>
    /// 是否正在下载（绑定到 ProgressButton.IsChecked）
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowIndeterminate))]
    private bool _isDownloading;

    /// <summary>
    /// 下载进度（0-100，绑定到 ProgressButton.Progress）
    /// </summary>
    [ObservableProperty]
    private int _downloadProgress;

    /// <summary>
    /// 下载状态文本（卡片底部显示，例如：12.3 MB / 45.6 MB）
    /// </summary>
    [ObservableProperty]
    private string _downloadStatus = string.Empty;

    /// <summary>
    /// 已下载文件最终路径（下载完成后填充）
    /// </summary>
    [ObservableProperty]
    private string? _downloadedFilePath;

    /// <summary>
    /// 当前最新版本对应的包是否已在本地存在（加载 / 下载完成后更新）
    /// </summary>
    [ObservableProperty]
    private bool _isDownloaded;

    /// <summary>
    /// 是否正在解压（下载完成后自动解压到 SourcePath）
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowIndeterminate))]
    private bool _isExtracting;

    /// <summary>
    /// 总大小是否未知（响应未返回 Content-Length）；用于驱动进度条切换为不确定动画
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowIndeterminate))]
    private bool _isTotalUnknown;

    /// <summary>
    /// 进度条是否需要显示为不确定模式（解压中 或 总大小未知的下载阶段）
    /// </summary>
    public bool ShowIndeterminate => IsExtracting || (IsDownloading && IsTotalUnknown);

    /// <summary>
    /// 解压后的目标路径（供后续打开源码目录等使用）
    /// </summary>
    [ObservableProperty]
    private string? _extractedPath;

    /// <summary>
    /// 下载取消令牌源（切换页面不取消，整个 ViewModel 单例随应用存活）
    /// </summary>
    public CancellationTokenSource? DownloadCts { get; set; }

    public RepositoryReleaseItem(RepositoryConfig repository)
    {
        Repository = repository;
    }
}

/// <summary>
/// Release 简要信息（用于列表显示）
/// </summary>
public class ReleaseSummary
{
    public string TagName { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string PublishedAt { get; init; } = string.Empty;
    public string HtmlUrl { get; init; } = string.Empty;
}
