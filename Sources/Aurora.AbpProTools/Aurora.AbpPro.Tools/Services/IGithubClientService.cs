using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Aurora.AbpPro.Tools.Models;

namespace Aurora.AbpPro.Tools.Services;

/// <summary>
/// GitHub 客户端服务：基于 Octokit 提供 Release 查询与资产下载能力；
/// 当配置 Token 时使用认证调用（提升速率限制与访问私有仓库），否则匿名调用。
/// </summary>
public interface IGithubClientService
{
    /// <summary>
    /// 获取仓库的 Release 列表
    /// </summary>
    /// <param name="repository">仓库配置（含 Owner / RepositoryId / Token）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<IReadOnlyList<Octokit.Release>> GetReleasesAsync(
        RepositoryConfig repository,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 获取仓库的最新 Release
    /// </summary>
    Task<Octokit.Release> GetLatestReleaseAsync(
        RepositoryConfig repository,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 按 Tag 获取指定 Release
    /// </summary>
    Task<Octokit.Release> GetReleaseByTagAsync(
        RepositoryConfig repository,
        string tag,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 下载 Release 的源代码 zip 包到指定目录；
    /// 当 <paramref name="repository"/> 提供 Token 时，使用 Bearer 认证下载，可访问私有仓库。
    /// 支持断点续传：先下载到 <c>{fileName}.partial</c>，完成后重命名为 <paramref name="fileName"/>。
    /// </summary>
    /// <param name="repository">仓库配置</param>
    /// <param name="release">要下载的 Release</param>
    /// <param name="targetDirectory">目标目录（不存在会自动创建）</param>
    /// <param name="fileName">最终保存的文件名（含扩展名）；为空时根据响应/URL 推断</param>
    /// <param name="progress">下载进度回调（已下载字节, 总字节，总字节为空时表示长度未知）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>下载完成的本地文件完整路径</returns>
    Task<string> DownloadSourceZipAsync(
        RepositoryConfig repository,
        Octokit.Release release,
        string targetDirectory,
        string? fileName = null,
        IProgress<(long Downloaded, long? Total)>? progress = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 下载 Release 的指定资产（asset）到指定目录
    /// </summary>
    Task<string> DownloadAssetAsync(
        RepositoryConfig repository,
        Octokit.ReleaseAsset asset,
        string targetDirectory,
        IProgress<(long Downloaded, long? Total)>? progress = null,
        CancellationToken cancellationToken = default
    );
}
