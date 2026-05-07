using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Aurora.AbpPro.Tools.Models;
using Aurora.AbpPro.Tools.ViewModels;
using Microsoft.Extensions.Logging;

namespace Aurora.AbpPro.Tools.Services;

/// <summary>
/// 框架下载服务实现：按依赖链顺序处理 4 个仓库
/// 1. abp-vnext-pro/abp（最新 Release） → 解析 aspnet-core/**/*.targets
/// 2. dotnetcore/CAP（版本来源 1） → 写入版本映射
/// 3. abpframework/abp（版本来源 1） → 解析根目录 *.props
/// 4. HangfireIO/Hangfire（版本来源 3） → 写入版本映射
/// </summary>
public class FrameworkDownloadService : IFrameworkDownloadService
{
    private readonly ILogger<FrameworkDownloadService> _logger;
    private readonly IGithubClientService _githubClient;
    private readonly IAppSettingsService _appSettings;
    private readonly ReadonlyConfig _readonlyConfig;

    public FrameworkDownloadService(
        ILogger<FrameworkDownloadService> logger,
        IGithubClientService githubClient,
        IAppSettingsService appSettings,
        ReadonlyConfig readonlyConfig
    )
    {
        _logger = logger;
        _githubClient = githubClient;
        _appSettings = appSettings;
        _readonlyConfig = readonlyConfig;
    }

    /// <inheritdoc />
    public async Task RunAsync(
        IReadOnlyList<RepositoryReleaseItem> items,
        CancellationToken cancellationToken = default
    )
    {
        // 按依赖链顺序定位 4 个仓库
        var abpVNextPro = FindRepo(items, "abp-vnext-pro", "abp");
        var cap = FindRepo(items, "dotnetcore", "CAP");
        var abpFramework = FindRepo(items, "abpframework", "abp");
        var hangfire = FindRepo(items, "HangfireIO", "Hangfire");

        // 阶段 1：abp-vnext-pro/abp 最新 Release
        var abpVNextProDir = await ProcessAsync(abpVNextPro, version: null, cancellationToken);

        // 阶段 2：从 abp-vnext-pro 的 *.targets 提取 CAP / abpframework 版本
        var (capVersion, abpVersion) = ParseAbpVNextProTargets(abpVNextProDir);
        _logger.LogInformation(
            "依赖版本解析：DotNetCore.CAP={Cap}，Volo.Abp={Abp}",
            capVersion ?? "(未找到)",
            abpVersion ?? "(未找到)"
        );

        // 阶段 3：dotnetcore/CAP 指定版本
        if (cap != null)
        {
            await ProcessAsync(cap, capVersion, cancellationToken);
        }

        // 阶段 4：abpframework/abp 指定版本
        string? abpFrameworkDir = null;
        if (abpFramework != null)
        {
            abpFrameworkDir = await ProcessAsync(abpFramework, abpVersion, cancellationToken);
        }

        // 阶段 5：从 abpframework 根目录 *.props 提取 Hangfire 版本
        string? hangfireVersion = null;
        if (!string.IsNullOrEmpty(abpFrameworkDir))
        {
            hangfireVersion = ParseAbpFrameworkPropsForHangfire(abpFrameworkDir);
            _logger.LogInformation(
                "依赖版本解析：Hangfire={Hangfire}",
                hangfireVersion ?? "(未找到)"
            );
        }

        // 阶段 6：HangfireIO/Hangfire 指定版本
        if (hangfire != null)
        {
            await ProcessAsync(hangfire, hangfireVersion, cancellationToken);
        }
    }

    /// <summary>
    /// 在卡片列表中按 Owner/RepositoryId 精准查找
    /// </summary>
    private static RepositoryReleaseItem? FindRepo(
        IReadOnlyList<RepositoryReleaseItem> items,
        string owner,
        string repositoryId
    )
    {
        return items.FirstOrDefault(i =>
            string.Equals(i.Repository.Owner, owner, StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                i.Repository.RepositoryId,
                repositoryId,
                StringComparison.OrdinalIgnoreCase
            )
        );
    }

    /// <summary>
    /// 处理单个仓库：下载 → 解压 → 写入版本映射；返回最终解压目录（失败返回 null）
    /// </summary>
    private async Task<string?> ProcessAsync(
        RepositoryReleaseItem? item,
        string? version,
        CancellationToken cancellationToken
    )
    {
        if (item == null)
        {
            return null;
        }
        try
        {
            item.ErrorMessage = null;
            item.DownloadProgress = 0;
            item.DownloadStatus = "准备下载...";

            // 同步 Token
            var token = _appSettings.Current.GitHubToken;
            if (!string.IsNullOrWhiteSpace(token))
            {
                item.Repository.Token = token;
            }

            // 1) 解析 Release
            Octokit.Release release;
            if (string.IsNullOrWhiteSpace(version))
            {
                _logger.LogInformation(
                    "[{Owner}/{Repo}] 获取最新 Release",
                    item.Repository.Owner,
                    item.Repository.RepositoryId
                );
                release = await _githubClient.GetLatestReleaseAsync(
                    item.Repository,
                    cancellationToken
                );
            }
            else
            {
                _logger.LogInformation(
                    "[{Owner}/{Repo}] 按 Tag 获取 Release：{Tag}",
                    item.Repository.Owner,
                    item.Repository.RepositoryId,
                    version
                );
                release = await ResolveReleaseByVersionAsync(
                    item.Repository,
                    version,
                    cancellationToken
                );
            }

            item.LatestRelease = release;
            var resolvedVersion = release.TagName ?? version ?? string.Empty;
            item.Repository.Version = resolvedVersion;
            // 实际使用版本：依赖链推导出的真实下载版本（与「最新版本」可能不同）
            item.UsedVersion = resolvedVersion;
            // 仅当本仓库版本来源是「自身最新 Release」时，才同步刷新最新版本展示，
            // 避免被依赖链版本覆盖导致 UI 误显示
            if (item.Repository.VersionSource == VersionSource.LatestRelease)
            {
                item.LatestTag = resolvedVersion;
                item.LatestName = string.IsNullOrWhiteSpace(release.Name)
                    ? resolvedVersion
                    : release.Name;
                item.PublishedAt =
                    release.PublishedAt?.LocalDateTime.ToString("yyyy-MM-dd HH:mm") ?? string.Empty;
            }

            // 2) 下载 zip：Owner_RepositoryId_Version.zip
            var fileName =
                $"{item.Repository.Owner}_{item.Repository.RepositoryId}_{resolvedVersion}.zip";
            var zipPath = Path.Combine(_readonlyConfig.DownloadPath, fileName);

            // 复用判定：本地 zip 存在 → 校验 MD5（与 Hash 文件一致）+ ZIP 头可读，通过则直接复用
            var hashFile = GetHashFilePath(item.Repository, resolvedVersion);
            string reuseReason = string.Empty;
            var canReuse =
                File.Exists(zipPath) && IsZipReusable(zipPath, hashFile, out reuseReason);
            if (canReuse)
            {
                _logger.LogInformation(
                    "[{Owner}/{Repo}] 复用本地 zip：{Path}",
                    item.Repository.Owner,
                    item.Repository.RepositoryId,
                    zipPath
                );
                item.DownloadStatus =
                    $"已存在本地包（{FormatSize(new FileInfo(zipPath).Length)}），跳过下载";
                item.DownloadProgress = 100;
                item.IsDownloaded = true;
            }
            else
            {
                if (File.Exists(zipPath))
                {
                    _logger.LogWarning(
                        "[{Owner}/{Repo}] 本地 zip 校验未通过（{Reason}），重新下载",
                        item.Repository.Owner,
                        item.Repository.RepositoryId,
                        reuseReason
                    );
                    TryDelete(zipPath);
                }

                item.IsDownloading = true;
                item.IsTotalUnknown = false;
                item.DownloadStatus = "正在下载...";

                // 速率/ETA 计算上下文
                var startTicks = Environment.TickCount64;
                long lastReportedBytes = 0;
                long lastReportedTicks = startTicks;
                double currentBytesPerSecond = 0;

                var progress = new Progress<(long Downloaded, long? Total)>(p =>
                {
                    // 计算瞬时速率（基于上次回调间隔，平滑窗口约 500ms）
                    var nowTicks = Environment.TickCount64;
                    var deltaMs = nowTicks - lastReportedTicks;
                    if (deltaMs >= 500)
                    {
                        currentBytesPerSecond =
                            (p.Downloaded - lastReportedBytes) * 1000.0 / Math.Max(deltaMs, 1);
                        lastReportedBytes = p.Downloaded;
                        lastReportedTicks = nowTicks;
                    }

                    if (p.Total is long total && total > 0)
                    {
                        item.IsTotalUnknown = false;
                        item.DownloadProgress = (int)(p.Downloaded * 100 / total);
                        var etaText = FormatEta(p.Downloaded, total, currentBytesPerSecond);
                        item.DownloadStatus =
                            $"{FormatSize(p.Downloaded)} / {FormatSize(total)} "
                            + $"({item.DownloadProgress}%) · {FormatSpeed(currentBytesPerSecond)}{etaText}";
                    }
                    else
                    {
                        item.IsTotalUnknown = true;
                        item.DownloadStatus =
                            $"{FormatSize(p.Downloaded)}（总大小未知）· {FormatSpeed(currentBytesPerSecond)}";
                    }
                });

                try
                {
                    zipPath = await _githubClient.DownloadSourceZipAsync(
                        item.Repository,
                        release,
                        _readonlyConfig.DownloadPath,
                        fileName,
                        progress,
                        cancellationToken
                    );
                }
                finally
                {
                    item.IsDownloading = false;
                    item.IsTotalUnknown = false;
                }

                // 下载完成 → ZIP 头校验
                if (!IsValidZip(zipPath, out var zipError))
                {
                    TryDelete(zipPath);
                    throw new InvalidDataException($"下载的 zip 不可读，已删除：{zipError}");
                }

                // 计算并持久化 MD5
                var md5 = await ComputeMd5Async(zipPath, cancellationToken);
                Directory.CreateDirectory(Path.GetDirectoryName(hashFile)!);
                File.WriteAllText(hashFile, md5);
                _logger.LogInformation(
                    "[{Owner}/{Repo}] MD5={Md5}",
                    item.Repository.Owner,
                    item.Repository.RepositoryId,
                    md5
                );

                item.DownloadedFilePath = zipPath;
                item.DownloadProgress = 100;
                item.IsDownloaded = true;
                item.DownloadStatus =
                    $"下载完成（{FormatSize(new FileInfo(zipPath).Length)}），MD5 已记录";
            }

            // 3) 解压（已存在 .complete 标记则跳过）
            var extractFolderName =
                $"{item.Repository.Owner}_{item.Repository.RepositoryId}_{resolvedVersion}";
            var extractDir = Path.Combine(_readonlyConfig.SourcePath, extractFolderName);
            var completeMarker = Path.Combine(extractDir, ".complete");
            if (Directory.Exists(extractDir) && File.Exists(completeMarker))
            {
                _logger.LogInformation(
                    "[{Owner}/{Repo}] 已存在解压标记，跳过解压：{Dir}",
                    item.Repository.Owner,
                    item.Repository.RepositoryId,
                    extractDir
                );
                item.ExtractedPath = extractDir;
                item.Repository.ExtractDirectory = extractDir;
                item.DownloadStatus = $"复用已解压目录 {extractDir}";
            }
            else
            {
                item.IsExtracting = true;
                item.DownloadStatus = "正在解压...";
                try
                {
                    await ExtractAsync(zipPath, extractDir, cancellationToken);
                    // 解压成功后写入完整性标记
                    await File.WriteAllTextAsync(
                        completeMarker,
                        DateTime.Now.ToString("O"),
                        cancellationToken
                    );
                }
                finally
                {
                    item.IsExtracting = false;
                }

                item.ExtractedPath = extractDir;
                item.Repository.ExtractDirectory = extractDir;
                item.DownloadStatus = $"已解压到 {extractDir}";
            }

            // 4) 写入版本映射文件
            WriteVersionMapping(item.Repository, resolvedVersion, extractFolderName);

            return extractDir;
        }
        catch (OperationCanceledException)
        {
            item.DownloadStatus = "已取消";
            throw;
        }
        catch (Exception ex)
        {
            item.ErrorMessage = ex.Message;
            item.DownloadStatus = "处理失败";
            _logger.LogError(
                ex,
                "[{Owner}/{Repo}] 下载/解压失败",
                item.Repository.Owner,
                item.Repository.RepositoryId
            );
            return null;
        }
    }

    /// <summary>
    /// 通过版本号定位 Release：依次尝试 v{ver} / {ver} / 包含匹配
    /// </summary>
    private async Task<Octokit.Release> ResolveReleaseByVersionAsync(
        RepositoryConfig repo,
        string version,
        CancellationToken ct
    )
    {
        var candidates = new[] { version, $"v{version}" };
        foreach (var tag in candidates)
        {
            try
            {
                return await _githubClient.GetReleaseByTagAsync(repo, tag, ct);
            }
            catch (Octokit.NotFoundException)
            {
                // 继续尝试下一个候选 Tag
            }
        }
        // 兜底：拉全量列表后做包含匹配
        var all = await _githubClient.GetReleasesAsync(repo, ct);
        var match = all.FirstOrDefault(r =>
            r.TagName != null && r.TagName.Contains(version, StringComparison.OrdinalIgnoreCase)
        );
        if (match != null)
        {
            return match;
        }
        throw new InvalidOperationException(
            $"未在 {repo.Owner}/{repo.RepositoryId} 找到匹配版本 {version} 的 Release"
        );
    }

    /// <summary>
    /// 解压 ZIP 到目标目录（已存在则先清空）
    /// </summary>
    private static Task ExtractAsync(string zipPath, string targetDir, CancellationToken ct)
    {
        return Task.Run(
            () =>
            {
                if (Directory.Exists(targetDir))
                {
                    Directory.Delete(targetDir, recursive: true);
                }
                Directory.CreateDirectory(targetDir);
                ZipFile.ExtractToDirectory(zipPath, targetDir, overwriteFiles: true);
            },
            ct
        );
    }

    /// <summary>
    /// 写入版本映射文件（JSON 格式：版本号 + 解压目录名）。
    /// 文件命名规则：abp-vnext-pro/abp → Data\abp-vnext-pro_abp；其余仓库 → Data\{Owner}
    /// </summary>
    private void WriteVersionMapping(
        RepositoryConfig repo,
        string version,
        string extractFolderName
    )
    {
        var path = GetVersionMappingPath(repo);
        var payload = new
        {
            Owner = repo.Owner,
            Repository = repo.RepositoryId,
            Version = version,
            ExtractDirectory = extractFolderName,
            UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        };
        var json = JsonSerializer.Serialize(
            payload,
            new JsonSerializerOptions { WriteIndented = true }
        );
        File.WriteAllText(path, json);
        _logger.LogInformation("写入版本映射：{Path}", path);
    }

    /// <summary>
    /// 计算指定仓库对应的版本映射文件路径
    /// </summary>
    private string GetVersionMappingPath(RepositoryConfig repo)
    {
        var fileName = string.Equals(
            repo.Owner,
            "abp-vnext-pro",
            StringComparison.OrdinalIgnoreCase
        )
            ? $"{repo.Owner}_{repo.RepositoryId}"
            : repo.Owner;
        return Path.Combine(_readonlyConfig.DataPath, fileName);
    }

    /// <inheritdoc />
    public string? TryReadUsedVersion(RepositoryConfig repo)
    {
        try
        {
            var path = GetVersionMappingPath(repo);
            if (!File.Exists(path))
            {
                return null;
            }
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.TryGetProperty("Version", out var v))
            {
                return v.GetString();
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "读取版本映射失败：{Owner}/{Repo}",
                repo.Owner,
                repo.RepositoryId
            );
            return null;
        }
    }

    /// <summary>
    /// 解析 abp-vnext-pro 解压目录中 aspnet-core 下所有 *.targets，提取 CAP 与 Volo.Abp 版本
    /// </summary>
    private (string? CapVersion, string? AbpVersion) ParseAbpVNextProTargets(string? extractDir)
    {
        if (string.IsNullOrEmpty(extractDir) || !Directory.Exists(extractDir))
        {
            return (null, null);
        }

        // GitHub 源码 zip 解压后通常多一层「{owner}-{repo}-{shortsha}」根目录
        var aspnetCoreDir = FindFirstSubdirectory(extractDir, "aspnet-core");
        if (string.IsNullOrEmpty(aspnetCoreDir))
        {
            _logger.LogWarning("未在解压目录找到 aspnet-core 子目录：{Dir}", extractDir);
            return (null, null);
        }

        string? capVersion = null;
        string? abpVersion = null;
        foreach (
            var targetsFile in Directory.EnumerateFiles(
                aspnetCoreDir,
                "*.targets",
                SearchOption.AllDirectories
            )
        )
        {
            try
            {
                var doc = XDocument.Load(targetsFile);
                foreach (
                    var pkg in doc.Descendants().Where(e => e.Name.LocalName == "PackageReference")
                )
                {
                    var name =
                        (string?)pkg.Attribute("Update") ?? (string?)pkg.Attribute("Include");
                    var ver =
                        (string?)pkg.Attribute("Version")
                        ?? pkg.Elements().FirstOrDefault(x => x.Name.LocalName == "Version")?.Value;
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(ver))
                    {
                        continue;
                    }
                    if (
                        capVersion == null
                        && name.StartsWith("DotNetCore.CAP", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        capVersion = ver;
                    }
                    if (
                        abpVersion == null
                        && name.StartsWith("Volo.Abp", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        abpVersion = ver;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "解析 targets 失败：{File}", targetsFile);
            }
            if (capVersion != null && abpVersion != null)
            {
                break;
            }
        }
        return (capVersion, abpVersion);
    }

    /// <summary>
    /// 解析 abpframework 解压目录根的 *.props，提取 Hangfire 版本
    /// </summary>
    private string? ParseAbpFrameworkPropsForHangfire(string extractDir)
    {
        if (!Directory.Exists(extractDir))
        {
            return null;
        }
        // 进入 GitHub 源码 zip 多出的一层根目录
        var rootDir = Directory.GetDirectories(extractDir).FirstOrDefault() ?? extractDir;

        foreach (
            var propsFile in Directory.EnumerateFiles(
                rootDir,
                "*.props",
                SearchOption.TopDirectoryOnly
            )
        )
        {
            try
            {
                var doc = XDocument.Load(propsFile);
                // 1) 优先从 PackageReference / PackageVersion 节点匹配 Hangfire*
                foreach (
                    var pkg in doc.Descendants()
                        .Where(e =>
                            e.Name.LocalName == "PackageReference"
                            || e.Name.LocalName == "PackageVersion"
                        )
                )
                {
                    var name =
                        (string?)pkg.Attribute("Update") ?? (string?)pkg.Attribute("Include");
                    var ver =
                        (string?)pkg.Attribute("Version")
                        ?? pkg.Elements().FirstOrDefault(x => x.Name.LocalName == "Version")?.Value;
                    if (
                        !string.IsNullOrWhiteSpace(name)
                        && name.StartsWith("Hangfire", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(ver)
                    )
                    {
                        return ver;
                    }
                }
                // 2) 兜底：匹配类似 <HangfireVersion>1.8.x</HangfireVersion> 的属性节点
                var versionProp = doc.Descendants()
                    .FirstOrDefault(e =>
                        e.Name.LocalName.StartsWith("Hangfire", StringComparison.OrdinalIgnoreCase)
                        && e.Name.LocalName.EndsWith("Version", StringComparison.OrdinalIgnoreCase)
                    );
                if (versionProp != null && !string.IsNullOrWhiteSpace(versionProp.Value))
                {
                    return versionProp.Value.Trim();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "解析 props 失败：{File}", propsFile);
            }
        }
        return null;
    }

    /// <summary>
    /// 在 root 目录下递归寻找第一个名为 <paramref name="name"/> 的子目录
    /// </summary>
    private static string? FindFirstSubdirectory(string root, string name)
    {
        try
        {
            return Directory
                .EnumerateDirectories(root, name, SearchOption.AllDirectories)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
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
    /// 速率格式化（B/s → KB/s → MB/s）
    /// </summary>
    private static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond <= 0)
        {
            return "0 B/s";
        }
        if (bytesPerSecond < 1024)
        {
            return $"{bytesPerSecond:F0} B/s";
        }
        if (bytesPerSecond < 1024 * 1024)
        {
            return $"{bytesPerSecond / 1024:F1} KB/s";
        }
        return $"{bytesPerSecond / 1024 / 1024:F2} MB/s";
    }

    /// <summary>
    /// 计算预计剩余时间文本（速率为 0 或总大小未知时返回空串）
    /// </summary>
    private static string FormatEta(long downloaded, long total, double bytesPerSecond)
    {
        if (bytesPerSecond <= 0 || total <= downloaded)
        {
            return string.Empty;
        }
        var remainingSec = (total - downloaded) / bytesPerSecond;
        if (remainingSec >= 3600)
        {
            return $" · 剩余 {remainingSec / 3600:F1} h";
        }
        if (remainingSec >= 60)
        {
            return $" · 剩余 {remainingSec / 60:F1} min";
        }
        return $" · 剩余 {remainingSec:F0} s";
    }

    /// <summary>
    /// 获取仓库 + 版本对应的 MD5 持久化文件路径：Data\Hash\{Owner}_{Repo}_{Version}.md5
    /// </summary>
    private string GetHashFilePath(RepositoryConfig repo, string version)
    {
        var dir = Path.Combine(_readonlyConfig.DataPath, "Hash");
        return Path.Combine(dir, $"{repo.Owner}_{repo.RepositoryId}_{version}.md5");
    }

    /// <summary>
    /// 判定本地 zip 是否可复用：Hash 文件存在 → 比对 MD5；否则仅做 ZIP 头校验作为兜底。
    /// </summary>
    private bool IsZipReusable(string zipPath, string hashFile, out string reason)
    {
        reason = string.Empty;
        if (!IsValidZip(zipPath, out var err))
        {
            reason = $"ZIP 头不可读：{err}";
            return false;
        }
        if (!File.Exists(hashFile))
        {
            reason = "未找到 MD5 记录";
            return false;
        }
        try
        {
            var expected = File.ReadAllText(hashFile).Trim();
            var actual = ComputeMd5(zipPath);
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"MD5 不匹配（期望 {expected} 实际 {actual}）";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            reason = $"MD5 校验异常：{ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// 通过尝试打开 ZIP 文件检验完整性
    /// </summary>
    private static bool IsValidZip(string zipPath, out string error)
    {
        error = string.Empty;
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            // 强制访问 entries 触发中央目录解析
            _ = zip.Entries.Count;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// 同步计算文件 MD5（小文件快）
    /// </summary>
    private static string ComputeMd5(string path)
    {
        using var stream = File.OpenRead(path);
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// 异步计算文件 MD5（大文件不阻塞 UI 线程）
    /// </summary>
    private static async Task<string> ComputeMd5Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        using var md5 = System.Security.Cryptography.MD5.Create();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            md5.TransformBlock(buffer, 0, read, null, 0);
        }
        md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(md5.Hash!).ToLowerInvariant();
    }

    /// <summary>
    /// 静默删除文件
    /// </summary>
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 忽略
        }
    }
}
