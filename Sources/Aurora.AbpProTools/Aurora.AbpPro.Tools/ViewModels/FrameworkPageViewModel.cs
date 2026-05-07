using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aurora.AbpPro.Tools.Models;
using Aurora.AbpPro.Tools.Pages;
using Aurora.AbpPro.Tools.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Aurora.AbpPro.Tools.ViewModels;

/// <summary>
/// 框架页 ViewModel：以卡片形式展示已注册仓库的 Release 信息，支持动态加载、刷新、下载与断点续传
/// </summary>
public partial class FrameworkPageViewModel : ObservableObject
{
    private readonly ILogger<FrameworkPageViewModel> _logger;
    private readonly IGithubClientService _githubClient;
    private readonly IAppSettingsService _appSettings;
    private readonly ReadonlyConfig _readonlyConfig;
    private readonly IFrameworkUpdateService _frameworkUpdateService;
    private readonly IFrameworkBuildService _frameworkBuildService;
    private readonly IFrameworkDownloadService _frameworkDownloadService;
    private readonly IFrameworkReadService _frameworkReadService;
    private readonly IFrameworkGenerateService _frameworkGenerateService;

    /// <summary>
    /// 仓库卡片列表（动态加载，可后续追加新仓库）
    /// </summary>
    public ObservableCollection<RepositoryReleaseItem> Items { get; } = new();

    /// <summary>
    /// 是否首次加载完成（防止重复触发）
    /// </summary>
    private bool _initialized;

    /// <summary>
    /// 是否已完成一次「刷新全部」加载（成功获取到 Release 信息后置 true）。
    /// 仅在该状态为 true 时，「更新框架」按钮才可用。
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadFrameworkCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReadFrameworkCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildFrameworkCommand))]
    private bool _allLoaded;

    /// <summary>
    /// 是否正在执行「下载框架」（用于禁用按钮、避免重入）
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadFrameworkCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReadFrameworkCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildFrameworkCommand))]
    private bool _isDownloadingFramework;

    /// <summary>
    /// 是否正在执行「生成框架」（用于禁用按钮、避免重入）
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadFrameworkCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReadFrameworkCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildFrameworkCommand))]
    private bool _isBuildingFramework;

    /// <summary>
    /// 是否正在执行「读取框架」（依赖收集流水线）
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReadFrameworkCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadFrameworkCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildFrameworkCommand))]
    private bool _isReadingFramework;

    public FrameworkPageViewModel(
        ILogger<FrameworkPageViewModel> logger,
        IGithubClientService githubClient,
        IAppSettingsService appSettings,
        ReadonlyConfig readonlyConfig,
        IFrameworkUpdateService frameworkUpdateService,
        IFrameworkBuildService frameworkBuildService,
        IFrameworkDownloadService frameworkDownloadService,
        IFrameworkReadService frameworkReadService,
        IFrameworkGenerateService frameworkGenerateService,
        IEnumerable<RepositoryConfig> repositories
    )
    {
        _logger = logger;
        _githubClient = githubClient;
        _appSettings = appSettings;
        _readonlyConfig = readonlyConfig;
        _frameworkUpdateService = frameworkUpdateService;
        _frameworkBuildService = frameworkBuildService;
        _frameworkDownloadService = frameworkDownloadService;
        _frameworkReadService = frameworkReadService;
        _frameworkGenerateService = frameworkGenerateService;
        foreach (var repo in repositories)
        {
            Items.Add(new RepositoryReleaseItem(repo));
        }
    }

    /// <summary>
    /// 页面加载时自动触发：并发加载各仓库的 Release 信息
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_initialized)
        {
            return;
        }
        _initialized = true;
        await RefreshAllAsync();
    }

    /// <summary>
    /// 手动刷新全部仓库
    /// </summary>
    [RelayCommand]
    private async Task RefreshAllAsync()
    {
        _logger.LogInformation("开始加载 {Count} 个仓库的 Release 信息", Items.Count);
        AllLoaded = false;
        var tasks = Items.Select(LoadItemAsync).ToArray();
        await Task.WhenAll(tasks);
        // 全部仓库都成功获取到 Release 才允许「更新框架」
        AllLoaded =
            Items.Count > 0
            && Items.All(i => i.LatestRelease != null && string.IsNullOrEmpty(i.ErrorMessage));
        _logger.LogInformation("仓库 Release 信息加载完成，AllLoaded={AllLoaded}", AllLoaded);
    }

    /// <summary>
    /// 下载框架：按依赖链顺序下载并解压全部 4 个仓库的 Release，并写入版本映射文件。
    /// 仅在「刷新全部」成功后可用。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDownloadFramework))]
    private async Task DownloadFrameworkAsync()
    {
        IsDownloadingFramework = true;
        try
        {
            _logger.LogInformation("开始下载框架，共 {Count} 个仓库", Items.Count);
            await _frameworkDownloadService.RunAsync(Items);
            _logger.LogInformation("框架下载流程结束");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "下载框架流程异常");
        }
        finally
        {
            IsDownloadingFramework = false;
        }
    }

    /// <summary>
    /// 「下载框架」可执行条件：已完成一次刷新全部，且当前未在下载/生成/读取中
    /// </summary>
    private bool CanDownloadFramework() =>
        AllLoaded && !IsDownloadingFramework && !IsBuildingFramework && !IsReadingFramework;

    /// <summary>
    /// 调用 <see cref="IFrameworkUpdateService"/> 完成复制 + 重命名 + 内容替换三步流水线（保留以兼容旧调用）
    /// </summary>
    private async Task RunFrameworkPipelineAsync(RepositoryReleaseItem item)
    {
        try
        {
            item.DownloadStatus = "正在更新框架...";
            var progress = new Progress<(string Stage, int Percent)>(p =>
            {
                item.DownloadStatus = $"{p.Stage} ({p.Percent}%)";
                item.DownloadProgress = p.Percent;
            });
            await _frameworkUpdateService.UpdateAsync(item.Repository, progress);
            item.DownloadStatus = $"框架已更新到 {item.Repository.FrameworkPath}";
            _logger.LogInformation(
                "仓库 {Title} 框架更新完成：{Path}",
                item.Title,
                item.Repository.FrameworkPath
            );
        }
        catch (Exception ex)
        {
            item.ErrorMessage = ex.Message;
            item.DownloadStatus = "框架更新失败";
            _logger.LogError(ex, "仓库 {Title} 框架更新失败", item.Title);
        }
    }

    /// <summary>
    /// 读取框架：读取业务应用项目 PackageReference，并在本地框架源码目录中递归匹配需要的项目。
    /// 结果通过 NLog 输出。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanReadFramework))]
    private async Task ReadFrameworkAsync()
    {
        IsReadingFramework = true;
        try
        {
            _logger.LogInformation("开始执行「读取框架」依赖收集流水线");
            var result = await _frameworkReadService.ReadAsync(Items);
            _logger.LogInformation(
                "读取框架完成：种子={T} 全部项目={A} 包映射={M} 未匹配外部包={U}",
                result.SeedProjects.Count,
                result.AllProjects.Count,
                result.PackagePathMap.Count,
                result.UnresolvedPackages.Count
            );

            // 弹出依赖图窗口（必须在 UI 线程上构造 Window）
            var graphVm = new DependencyGraphViewModel(result);
            var graphWindow = new DependencyGraphWindow(graphVm)
            {
                Owner = System.Windows.Application.Current?.MainWindow,
            };
            graphWindow.Show();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取框架失败");
        }
        finally
        {
            IsReadingFramework = false;
        }
    }

    /// <summary>
    /// 「读取框架」可执行条件：已完成一次刷新全部，且当前不在下载/生成/读取中
    /// </summary>
    private bool CanReadFramework() =>
        AllLoaded && !IsReadingFramework && !IsDownloadingFramework && !IsBuildingFramework;

    /// <summary>
    /// 生成框架：先执行依赖收集，再把匹配到的完整项目文件夹平铺拷贝到 Sources\AuroraAbpPro，
    /// 完成后弹出结果窗口
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanBuildFramework))]
    private async Task BuildFrameworkAsync()
    {
        IsBuildingFramework = true;
        try
        {
            _logger.LogInformation("开始生成框架：先执行依赖收集");
            var readResult = await _frameworkReadService.ReadAsync(Items);
            _logger.LogInformation(
                "依赖收集完成：种子={S} 全部={A}，开始拷贝 AuroraAbpPro 项目",
                readResult.SeedProjects.Count,
                readResult.AllProjects.Count
            );
            var genResult = await _frameworkGenerateService.GenerateAsync(readResult);
            _logger.LogInformation(
                "生成框架完成：Success={Ok} Projects={P} Files={F} Csproj={C}",
                genResult.Success,
                genResult.CopiedProjectCount,
                genResult.CopiedFileCount,
                genResult.RewrittenCsprojCount
            );

            var window = new Pages.FrameworkGenerateResultWindow(genResult)
            {
                Owner = System.Windows.Application.Current?.MainWindow,
            };
            window.Show();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成框架失败");
        }
        finally
        {
            IsBuildingFramework = false;
        }
    }

    /// <summary>
    /// 「生成框架」可执行条件：已完成一次刷新全部，且当前不在构建 / 不在下载框架中
    /// </summary>
    private bool CanBuildFramework() =>
        AllLoaded && !IsBuildingFramework && !IsDownloadingFramework && !IsReadingFramework;

    /// <summary>
    /// 单个仓库的框架生成流水线（保留以兼容潜在调用，未在 UI 启用）
    /// </summary>
    private async Task BuildItemAsync(RepositoryReleaseItem item)
    {
        try
        {
            item.ErrorMessage = null;
            item.DownloadStatus = "正在生成框架...";
            var progress = new Progress<(string Stage, int Percent)>(p =>
            {
                item.DownloadStatus = $"{p.Stage} ({p.Percent}%)";
                item.DownloadProgress = p.Percent;
            });
            var result = await _frameworkBuildService.BuildAsync(item.Repository, progress);
            item.DownloadStatus =
                $"框架生成完成：目标 {result.TargetCount}，成功 {result.SucceededCount}，失败 {result.FailedCount}";
            _logger.LogInformation(
                "仓库 {Title} 框架生成完成：{Status}",
                item.Title,
                item.DownloadStatus
            );
        }
        catch (Exception ex)
        {
            item.ErrorMessage = ex.Message;
            item.DownloadStatus = "框架生成失败";
            _logger.LogError(ex, "仓库 {Title} 框架生成失败", item.Title);
        }
    }

    /// <summary>
    /// 单卡片刷新命令（用户手动点击单卡刷新按钮触发）
    /// </summary>
    [RelayCommand]
    private async Task RefreshItemAsync(RepositoryReleaseItem? item)
    {
        if (item == null)
        {
            return;
        }
        await LoadItemAsync(item);
    }

    /// <summary>
    /// 打开下载文件夹（不存在则先创建）
    /// </summary>
    [RelayCommand]
    private void OpenDownloadFolder()
    {
        try
        {
            string path = _readonlyConfig.DownloadPath;
            // 该路径明确是目录（即便名字像 .aurora.abp.pro 含「点」），直接 CreateDirectory 避免被启发式判定为文件
            System.IO.Directory.CreateDirectory(path);
            // 使用 explorer.exe 打开目录，兼容性最好；路径加引号防止空格被截断
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true,
                }
            );
            _logger.LogInformation("已打开下载文件夹：{Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "打开下载文件夹失败：{Path}", _readonlyConfig.DownloadPath);
        }
    }

    /// <summary>
    /// 下载/取消命令：未在下载时启动；正在下载时取消（保留 .partial 文件以供下次断点续传）
    /// </summary>
    [RelayCommand]
    private async Task DownloadAsync(RepositoryReleaseItem? item)
    {
        if (item == null)
        {
            return;
        }
        // 已在下载中：执行取消（不删 .partial，下次可继续）
        if (item.IsDownloading)
        {
            item.DownloadCts?.Cancel();
            return;
        }
        if (item.LatestRelease == null || string.IsNullOrWhiteSpace(item.Repository.Version))
        {
            item.ErrorMessage = "请先刷新获取最新版本";
            return;
        }

        var cts = new CancellationTokenSource();
        item.DownloadCts = cts;
        item.IsDownloading = true;
        item.DownloadProgress = 0;
        item.DownloadStatus = "准备下载...";
        item.ErrorMessage = null;

        // 命名规则：Owner_RepositoryId_Version.zip
        var fileName =
            $"{item.Repository.Owner}_{item.Repository.RepositoryId}_{item.Repository.Version}.zip";
        var progress = new Progress<(long Downloaded, long? Total)>(p =>
        {
            if (p.Total is long total && total > 0)
            {
                item.DownloadProgress = (int)(p.Downloaded * 100 / total);
                item.DownloadStatus =
                    $"{FormatSize(p.Downloaded)} / {FormatSize(total)} ({item.DownloadProgress}%)";
            }
            else
            {
                item.DownloadStatus = $"{FormatSize(p.Downloaded)}（总大小未知）";
            }
        });

        try
        {
            // 同步最新 Token（用户可能在下载前刚改过）
            var token = _appSettings.Current.GitHubToken;
            if (!string.IsNullOrWhiteSpace(token))
            {
                item.Repository.Token = token;
            }

            var path = await _githubClient.DownloadSourceZipAsync(
                item.Repository,
                item.LatestRelease,
                _readonlyConfig.DownloadPath,
                fileName,
                progress,
                cts.Token
            );
            item.DownloadedFilePath = path;
            item.DownloadProgress = 100;
            item.DownloadStatus = "下载完成";
            item.IsDownloaded = true;
            _logger.LogInformation("仓库 {Title} 下载完成：{Path}", item.Title, path);

            // 下载成功后自动解压到 SourcePath
            await ExtractAsync(item, path);
        }
        catch (OperationCanceledException)
        {
            item.DownloadStatus = "已取消（可继续下载）";
            _logger.LogInformation("仓库 {Title} 下载已取消", item.Title);
        }
        catch (Exception ex)
        {
            item.ErrorMessage = ex.Message;
            item.DownloadStatus = "下载失败";
            _logger.LogError(ex, "仓库 {Title} 下载失败", item.Title);
        }
        finally
        {
            item.IsDownloading = false;
            item.DownloadCts = null;
            cts.Dispose();
        }
    }

    /// <summary>
    /// 加载单个仓库的最近 Release，并把最新版本号写入 RepositoryConfig.Version
    /// </summary>
    private async Task LoadItemAsync(RepositoryReleaseItem item)
    {
        item.IsLoading = true;
        item.ErrorMessage = null;
        try
        {
            var token = _appSettings.Current.GitHubToken;

            // 商业版仓库：必须有 Token 才能访问（多为私有仓库）
            if (
                item.Repository.RepositoryType == RepositoryType.Business
                && string.IsNullOrWhiteSpace(token)
            )
            {
                throw new InvalidOperationException(
                    "商业版仓库需在“设置”页填入 GitHub Token 后才能访问"
                );
            }

            // 只要配置了 Token，统一使用认证调用（公开仓库匿名 60 次/小时，认证后 5000 次/小时）
            if (!string.IsNullOrWhiteSpace(token))
            {
                item.Repository.Token = token;
            }

            var releases = await _githubClient.GetReleasesAsync(item.Repository);
            item.RecentReleases.Clear();
            foreach (var release in releases.Take(5))
            {
                item.RecentReleases.Add(
                    new ReleaseSummary
                    {
                        TagName = release.TagName ?? string.Empty,
                        Name = string.IsNullOrWhiteSpace(release.Name)
                            ? release.TagName ?? string.Empty
                            : release.Name,
                        PublishedAt =
                            release.PublishedAt?.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
                            ?? string.Empty,
                        HtmlUrl = release.HtmlUrl ?? string.Empty,
                    }
                );
            }
            var latest = releases.FirstOrDefault();
            if (latest != null)
            {
                item.LatestRelease = latest;
                item.LatestTag = latest.TagName ?? string.Empty;
                item.LatestName = string.IsNullOrWhiteSpace(latest.Name)
                    ? item.LatestTag
                    : latest.Name;
                item.PublishedAt =
                    latest.PublishedAt?.LocalDateTime.ToString("yyyy-MM-dd HH:mm") ?? string.Empty;
                // 关键：把最新版本号写回 RepositoryConfig.Version，用于下载文件命名等
                item.Repository.Version = item.LatestTag;
                // 同步推断本地解压目录：SourcePath/{Owner}_{RepositoryId}_{Version}
                item.Repository.ExtractDirectory = System.IO.Path.Combine(
                    _readonlyConfig.SourcePath,
                    $"{item.Repository.Owner}_{item.Repository.RepositoryId}_{item.Repository.Version}"
                );

                item.Repository.FrameworkPath =
                    System.IO.Path.Combine(_readonlyConfig.BasePath, item.Repository.FrameworkName)
                    ?? string.Empty;
            }
            else
            {
                item.LatestRelease = null;
                item.LatestTag = "(无)";
                item.LatestName = "暂无 Release";
                item.PublishedAt = string.Empty;
                item.Repository.Version = string.Empty;
                item.Repository.ExtractDirectory = string.Empty;
                item.Repository.FrameworkPath = string.Empty;
            }

            // 先回填「实际使用版本」（依赖链锁定的版本），再据此判定本地下载状态
            var used = _frameworkDownloadService.TryReadUsedVersion(item.Repository);
            if (!string.IsNullOrWhiteSpace(used))
            {
                item.UsedVersion = used;
            }

            // 检测本地是否已下载（优先匹配实际使用版本，其次匹配最新版本）
            UpdateDownloadedState(item);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "加载仓库 Release 失败：{Owner}/{Repo}",
                item.Repository.Owner,
                item.Repository.RepositoryId
            );
            item.ErrorMessage = ex.Message;
        }
        finally
        {
            item.IsLoading = false;
        }
    }

    /// <summary>
    /// 解压 ZIP 包到 SourcePath/{Owner}_{RepositoryId}_{Version}/，已存在则先清空
    /// </summary>
    private async Task ExtractAsync(RepositoryReleaseItem item, string zipPath)
    {
        item.IsExtracting = true;
        var folderName =
            $"{item.Repository.Owner}_{item.Repository.RepositoryId}_{item.Repository.Version}";
        var targetDir = System.IO.Path.Combine(_readonlyConfig.SourcePath, folderName);
        try
        {
            item.DownloadStatus = "正在解压...";
            System.IO.Directory.CreateDirectory(_readonlyConfig.SourcePath);
            await Task.Run(() =>
            {
                // 已存在则先清理，保证幂等（带只读属性清除 + 重试，规避偶发文件占用）
                if (System.IO.Directory.Exists(targetDir))
                {
                    DeleteDirectoryWithRetry(targetDir);
                }
                System.IO.Directory.CreateDirectory(targetDir);
                System.IO.Compression.ZipFile.ExtractToDirectory(
                    zipPath,
                    targetDir,
                    overwriteFiles: true
                );
            });
            item.ExtractedPath = targetDir;
            item.DownloadStatus = $"已解压到 {targetDir}";
            _logger.LogInformation("仓库 {Title} 解压完成：{Path}", item.Title, targetDir);
        }
        catch (Exception ex)
        {
            item.ErrorMessage = ex.Message;
            item.DownloadStatus = "解压失败";
            _logger.LogError(ex, "仓库 {Title} 解压失败：{Zip}", item.Title, zipPath);
        }
        finally
        {
            item.IsExtracting = false;
        }
    }

    /// <summary>
    /// 根据当前 RepositoryConfig.Version / UsedVersion + DownloadPath 推断本地状态：
    /// 1) 优先匹配「实际使用版本」对应的 zip（依赖链锁定的版本，对绝大多数仓库才是真正"已下载"的版本）
    /// 2) 否则回退匹配「最新版本」对应的 zip
    /// 3) 若两者都缺失则显示"未下载"
    /// </summary>
    private void UpdateDownloadedState(RepositoryReleaseItem item)
    {
        // 候选版本顺序：实际使用版本（去掉前导 v）→ 最新版本
        var candidates = new[]
        {
            NormalizeVersion(item.UsedVersion),
            NormalizeVersion(item.Repository.Version),
        };

        foreach (var version in candidates)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                continue;
            }
            var fileName = $"{item.Repository.Owner}_{item.Repository.RepositoryId}_{version}.zip";
            var fullPath = System.IO.Path.Combine(_readonlyConfig.DownloadPath, fileName);
            if (!System.IO.File.Exists(fullPath))
            {
                continue;
            }

            item.IsDownloaded = true;
            item.DownloadedFilePath = fullPath;
            item.DownloadProgress = 100;
            var size = FormatSize(new System.IO.FileInfo(fullPath).Length);
            // 当本地 zip 对应的是"实际使用版本"且与"最新版本"不一致，明确告诉用户当前是依赖锁定版
            var latest = NormalizeVersion(item.Repository.Version);
            if (
                !string.IsNullOrEmpty(latest)
                && !string.Equals(latest, version, StringComparison.OrdinalIgnoreCase)
            )
            {
                item.DownloadStatus = $"已下载实际使用版本 {version}（{size}）";
            }
            else
            {
                item.DownloadStatus = $"已下载到本地（{size}）";
            }
            return;
        }

        // 两个候选版本都没有本地 zip → 标记未下载
        item.IsDownloaded = false;
        item.DownloadedFilePath = null;
        if (string.IsNullOrEmpty(item.DownloadStatus) || item.DownloadStatus.StartsWith("已下载"))
        {
            item.DownloadStatus = string.Empty;
            item.DownloadProgress = 0;
        }
    }

    /// <summary>
    /// 归一化版本号：去掉前导 v / V，便于与文件名（无 v 前缀）匹配
    /// </summary>
    private static string NormalizeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return string.Empty;
        }
        var v = version.Trim();
        if (v.StartsWith('v') || v.StartsWith('V'))
        {
            v = v.Substring(1);
        }
        return v;
    }

    /// <summary>
    /// 字节数格式化
    /// </summary>
    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }
        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:F1} KB";
        }
        if (bytes < 1024L * 1024 * 1024)
        {
            return $"{bytes / 1024.0 / 1024:F1} MB";
        }
        return $"{bytes / 1024.0 / 1024 / 1024:F2} GB";
    }

    /// <summary>
    /// 健壮的目录递归删除：先递归清除只读属性，再以指数退避重试若干次，
    /// 规避 Windows 上「文件被另一进程占用 / 杀软扫描 / Explorer 缓存句柄」导致的偶发占用错误
    /// </summary>
    private void DeleteDirectoryWithRetry(string targetDir, int maxAttempts = 5)
    {
        // 1) 先把所有文件的只读位去掉，否则 Delete 会抛 UnauthorizedAccessException
        try
        {
            var dirInfo = new System.IO.DirectoryInfo(targetDir);
            foreach (var file in dirInfo.GetFiles("*", System.IO.SearchOption.AllDirectories))
            {
                if (file.IsReadOnly)
                {
                    file.IsReadOnly = false;
                }
                file.Attributes = System.IO.FileAttributes.Normal;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "重置只读属性失败：{Path}", targetDir);
        }

        // 2) 重试删除
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                System.IO.Directory.Delete(targetDir, recursive: true);
                return;
            }
            catch (Exception ex)
                when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
                if (attempt == maxAttempts)
                {
                    _logger.LogError(ex, "删除目录最终失败：{Path}", targetDir);
                    throw;
                }
                _logger.LogWarning(
                    "删除目录被占用，第 {Attempt}/{Max} 次重试：{Path}",
                    attempt,
                    maxAttempts,
                    targetDir
                );
                // 指数退避：200ms / 400ms / 800ms ...
                System.Threading.Thread.Sleep(200 * (int)Math.Pow(2, attempt - 1));
                // 触发一次 GC 释放可能未及时回收的文件句柄
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
    }
}
