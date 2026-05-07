using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Aurora.AbpPro.Tools.Models;
using Microsoft.Extensions.Logging;
using Octokit;

namespace Aurora.AbpPro.Tools.Services;

/// <summary>
/// GitHub 客户端服务实现：
/// - Octokit 客户端按 Token 维度缓存（同一 Token 复用同一 GitHubClient）；
/// - 下载使用共享 <see cref="HttpClient"/>，按需附加 Bearer 认证；
/// - 支持取消、进度回调；下载文件名优先取自响应头，回退到 URL。
/// </summary>
public class GithubClientService : IGithubClientService, IDisposable
{
    /// <summary>
    /// User-Agent，GitHub API / 资产下载均要求显式提供
    /// </summary>
    private const string UserAgent = "Aurora.AbpPro.Tools";

    /// <summary>
    /// 下载缓冲区大小（80KB，兼顾吞吐与进度更新频率）
    /// </summary>
    private const int DownloadBufferSize = 81920;

    private readonly ILogger<GithubClientService> _logger;

    /// <summary>
    /// 复用的 HttpClient（线程安全）
    /// </summary>
    private readonly HttpClient _httpClient;

    /// <summary>
    /// 按 Token（含空字符串代表匿名）缓存的 Octokit 客户端
    /// </summary>
    private readonly ConcurrentDictionary<string, GitHubClient> _clientCache = new();

    public GithubClientService(ILogger<GithubClientService> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Release>> GetReleasesAsync(
        RepositoryConfig repository,
        CancellationToken cancellationToken = default
    )
    {
        EnsureRepository(repository);
        var client = GetClient(repository.Token);
        _logger.LogInformation(
            "获取 Release 列表：{Owner}/{Repo}（认证：{HasAuth}）",
            repository.Owner,
            repository.RepositoryId,
            !string.IsNullOrWhiteSpace(repository.Token)
        );
        var list = await client.Repository.Release.GetAll(
            repository.Owner,
            repository.RepositoryId
        );
        cancellationToken.ThrowIfCancellationRequested();
        return list;
    }

    /// <inheritdoc />
    public async Task<Release> GetLatestReleaseAsync(
        RepositoryConfig repository,
        CancellationToken cancellationToken = default
    )
    {
        EnsureRepository(repository);
        var client = GetClient(repository.Token);
        _logger.LogInformation(
            "获取最新 Release：{Owner}/{Repo}",
            repository.Owner,
            repository.RepositoryId
        );
        var release = await client.Repository.Release.GetLatest(
            repository.Owner,
            repository.RepositoryId
        );
        cancellationToken.ThrowIfCancellationRequested();
        return release;
    }

    /// <inheritdoc />
    public async Task<Release> GetReleaseByTagAsync(
        RepositoryConfig repository,
        string tag,
        CancellationToken cancellationToken = default
    )
    {
        EnsureRepository(repository);
        if (string.IsNullOrWhiteSpace(tag))
        {
            throw new ArgumentException("tag 不能为空", nameof(tag));
        }
        var client = GetClient(repository.Token);
        _logger.LogInformation(
            "按 Tag 获取 Release：{Owner}/{Repo} -> {Tag}",
            repository.Owner,
            repository.RepositoryId,
            tag
        );
        var release = await client.Repository.Release.Get(
            repository.Owner,
            repository.RepositoryId,
            tag
        );
        cancellationToken.ThrowIfCancellationRequested();
        return release;
    }

    /// <inheritdoc />
    public Task<string> DownloadSourceZipAsync(
        RepositoryConfig repository,
        Release release,
        string targetDirectory,
        string? fileName = null,
        IProgress<(long Downloaded, long? Total)>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        EnsureRepository(repository);
        ArgumentNullException.ThrowIfNull(release);
        // 优先 zipball_url；某些场景没有 ZipballUrl 时回退到 codeload 链接
        var url = !string.IsNullOrWhiteSpace(release.ZipballUrl)
            ? release.ZipballUrl
            : $"https://codeload.github.com/{repository.Owner}/{repository.RepositoryId}/zip/refs/tags/{release.TagName}";
        var fallbackName = string.IsNullOrWhiteSpace(fileName)
            ? $"{repository.RepositoryId}-{release.TagName}.zip"
            : fileName!;
        return DownloadAsync(
            url,
            targetDirectory,
            fallbackName,
            preferredFileName: !string.IsNullOrWhiteSpace(fileName),
            repository.Token,
            acceptHeader: null,
            progress,
            cancellationToken
        );
    }

    /// <inheritdoc />
    public Task<string> DownloadAssetAsync(
        RepositoryConfig repository,
        ReleaseAsset asset,
        string targetDirectory,
        IProgress<(long Downloaded, long? Total)>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        EnsureRepository(repository);
        ArgumentNullException.ThrowIfNull(asset);
        // 通过 GitHub API 的 asset URL 下载需要 Accept: application/octet-stream
        // 直接走 BrowserDownloadUrl 则不需要特殊 Accept
        var url = asset.BrowserDownloadUrl;
        return DownloadAsync(
            url,
            targetDirectory,
            asset.Name,
            preferredFileName: true,
            repository.Token,
            acceptHeader: null,
            progress,
            cancellationToken
        );
    }

    /// <summary>
    /// 通用下载实现（带可选 Bearer 认证、进度回调、取消支持、断点续传）。
    /// 断点续传策略：下载到 <c>{file}.partial</c>，完成后原子性重命名；
    /// 启动时若存在部分文件则发送 <c>Range</c> 请求追加，服务端不支持时（返回 200）从头重试。
    /// </summary>
    private async Task<string> DownloadAsync(
        string url,
        string targetDirectory,
        string fallbackFileName,
        bool preferredFileName,
        string? token,
        string? acceptHeader,
        IProgress<(long Downloaded, long? Total)>? progress,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("下载地址不能为空", nameof(url));
        }
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            throw new ArgumentException("目标目录不能为空", nameof(targetDirectory));
        }

        Directory.CreateDirectory(targetDirectory);

        // 如果不强制使用传入的文件名，先发 HEAD-式探测获取响应头后再决定；
        // 为简化：如果传入了明确文件名，直接使用（便于断点续传定位 partial 文件）。
        var finalFileName = fallbackFileName;
        var finalPath = Path.Combine(targetDirectory, finalFileName);
        var partialPath = finalPath + ".partial";

        // 已完整存在则直接返回（幂等）
        if (File.Exists(finalPath))
        {
            _logger.LogInformation("文件已存在，跳过下载：{File}", finalPath);
            return finalPath;
        }

        // 续传元数据文件路径（记录 Url 与 TotalSize，避免远端重新打包后用旧片段拼出坏文件）
        var metaPath = partialPath + ".meta";

        // 存在部分下载文件时，先用 meta 校验是否与当前 URL 匹配，不匹配则直接清理
        long existingBytes = 0L;
        if (File.Exists(partialPath))
        {
            if (!IsPartialMetaMatch(metaPath, url, out _))
            {
                _logger.LogWarning("续传元数据不匹配，清理旧片段重新下载：{File}", partialPath);
                TryDelete(partialPath);
                TryDelete(metaPath);
            }
            else
            {
                existingBytes = new FileInfo(partialPath).Length;
            }
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        if (!string.IsNullOrWhiteSpace(acceptHeader))
        {
            request.Headers.Accept.ParseAdd(acceptHeader);
        }
        if (existingBytes > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingBytes, null);
            _logger.LogInformation(
                "检测到部分文件 {Bytes} 字节，尝试断点续传：{Url}",
                existingBytes,
                url
            );
        }
        else
        {
            _logger.LogInformation(
                "开始下载：{Url}（认证：{HasAuth}）",
                url,
                !string.IsNullOrWhiteSpace(token)
            );
        }

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        response.EnsureSuccessStatusCode();

        // 服务端是否接受了断点续传：返回 206 为接受，其他视为不支持
        var resumed =
            existingBytes > 0 && response.StatusCode == System.Net.HttpStatusCode.PartialContent;
        if (existingBytes > 0 && !resumed)
        {
            _logger.LogInformation("服务端不支持断点续传，从头下载：{Url}", url);
            existingBytes = 0; // 从 0 重新计数
        }

        // 总字节数：206 时 ContentLength 是剩余量，需加上已下载量
        long? totalBytes = response.Content.Headers.ContentLength;
        if (resumed && totalBytes.HasValue)
        {
            totalBytes += existingBytes;
        }

        // 写入/更新续传元数据，便于程序中断后下次启动校验
        WritePartialMeta(metaPath, url, totalBytes);

        // 如果传入了明确文件名则不从响应头/URL 推断；否则尝试从响应头取
        if (!preferredFileName)
        {
            var headerFileName =
                response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
            if (!string.IsNullOrWhiteSpace(headerFileName))
            {
                finalFileName = headerFileName;
                finalPath = Path.Combine(targetDirectory, finalFileName);
                partialPath = finalPath + ".partial";
            }
        }

        await using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        // resumed 时以 Append 打开；否则 Create 重写
        var fileMode = resumed ? System.IO.FileMode.Append : System.IO.FileMode.Create;
        await using var fileStream = new FileStream(
            partialPath,
            fileMode,
            FileAccess.Write,
            FileShare.None,
            DownloadBufferSize,
            useAsync: true
        );

        var buffer = new byte[DownloadBufferSize];
        long downloaded = existingBytes;
        progress?.Report((downloaded, totalBytes));
        int read;
        while (
            (
                read = await sourceStream.ReadAsync(
                    buffer.AsMemory(0, buffer.Length),
                    cancellationToken
                )
            ) > 0
        )
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            downloaded += read;
            progress?.Report((downloaded, totalBytes));
        }

        await fileStream.FlushAsync(cancellationToken);
        // 完成后重命名：先释放句柄（using 在下一轮作用域结束释放）需同步手动处理
        // 使用局部函数推迟重命名到方法末尾
        var path = await CompleteAsync(fileStream, partialPath, finalPath, downloaded);
        // 完成后清理 meta
        TryDelete(metaPath);
        return path;
    }

    /// <summary>
    /// 释放文件句柄并将 .partial 重命名为最终文件名
    /// </summary>
    private async Task<string> CompleteAsync(
        FileStream fileStream,
        string partialPath,
        string finalPath,
        long downloaded
    )
    {
        await fileStream.DisposeAsync();
        if (File.Exists(finalPath))
        {
            File.Delete(finalPath);
        }
        File.Move(partialPath, finalPath);
        _logger.LogInformation("下载完成：{File}（{Bytes} 字节）", finalPath, downloaded);
        return finalPath;
    }

    /// <summary>
    /// 校验 partial.meta 是否与当前 URL 匹配
    /// </summary>
    private bool IsPartialMetaMatch(string metaPath, string url, out long? totalSize)
    {
        totalSize = null;
        try
        {
            if (!File.Exists(metaPath))
            {
                return false;
            }
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(metaPath));
            var root = doc.RootElement;
            var savedUrl = root.TryGetProperty("Url", out var u) ? u.GetString() : null;
            if (!string.Equals(savedUrl, url, StringComparison.Ordinal))
            {
                return false;
            }
            if (
                root.TryGetProperty("TotalSize", out var t)
                && t.ValueKind == System.Text.Json.JsonValueKind.Number
            )
            {
                totalSize = t.GetInt64();
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取续传元数据失败：{File}", metaPath);
            return false;
        }
    }

    /// <summary>
    /// 写入/更新续传元数据
    /// </summary>
    private void WritePartialMeta(string metaPath, string url, long? totalSize)
    {
        try
        {
            var payload = new
            {
                Url = url,
                TotalSize = totalSize,
                UpdatedAt = DateTime.Now,
            };
            File.WriteAllText(
                metaPath,
                System.Text.Json.JsonSerializer.Serialize(
                    payload,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
                )
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "写入续传元数据失败：{File}", metaPath);
        }
    }

    /// <summary>
    /// 静默删除文件（不存在或异常都吞掉）
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

    /// <summary>
    /// 按 Token 获取（或创建并缓存）Octokit 客户端
    /// </summary>
    private GitHubClient GetClient(string? token)
    {
        var key = token ?? string.Empty;
        return _clientCache.GetOrAdd(
            key,
            t =>
            {
                var client = new GitHubClient(new Octokit.ProductHeaderValue(UserAgent));
                if (!string.IsNullOrWhiteSpace(t))
                {
                    // 使用 PAT 进行 Bearer 认证
                    client.Credentials = new Credentials(t);
                }
                return client;
            }
        );
    }

    /// <summary>
    /// 校验仓库配置必填字段
    /// </summary>
    private static void EnsureRepository(RepositoryConfig repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (string.IsNullOrWhiteSpace(repository.Owner))
        {
            throw new ArgumentException("RepositoryConfig.Owner 不能为空", nameof(repository));
        }
        if (string.IsNullOrWhiteSpace(repository.RepositoryId))
        {
            throw new ArgumentException(
                "RepositoryConfig.RepositoryId 不能为空",
                nameof(repository)
            );
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
