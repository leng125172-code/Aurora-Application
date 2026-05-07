using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aurora.AbpPro.Tools.Models;
using Aurora.AbpPro.Tools.ViewModels;

namespace Aurora.AbpPro.Tools.Services;

/// <summary>
/// 框架下载服务：按依赖顺序下载 4 个仓库的 Release，
/// 解压后写入版本映射文件，并解析 *.targets / *.props 推导后续仓库版本。
/// </summary>
public interface IFrameworkDownloadService
{
    /// <summary>
    /// 按既定依赖链执行下载流水线（直接修改传入的 <see cref="RepositoryReleaseItem"/>，由 UI 数据绑定刷新进度）
    /// </summary>
    /// <param name="items">框架页中的卡片列表（顺序无关，内部会按依赖关系重新排序）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task RunAsync(
        IReadOnlyList<RepositoryReleaseItem> items,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 读取本地版本映射文件，返回该仓库上次实际下载使用的版本号；不存在返回 null
    /// </summary>
    string? TryReadUsedVersion(RepositoryConfig repo);
}
