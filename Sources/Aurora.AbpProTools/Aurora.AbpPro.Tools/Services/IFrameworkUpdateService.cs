using System;
using System.Threading;
using System.Threading.Tasks;
using Aurora.AbpPro.Tools.Models;

namespace Aurora.AbpPro.Tools.Services;

/// <summary>
/// 框架更新服务：将解压后的源码复制到 FrameworkPath，并按 ReplaceContent 规则
/// 完成「目录/文件名重命名」与「文件内容替换」三步流水线
/// </summary>
public interface IFrameworkUpdateService
{
    /// <summary>
    /// 执行单个仓库的框架更新流水线
    /// </summary>
    /// <param name="repository">仓库配置（要求已填充 ExtractDirectory、FrameworkPath、ReplaceContent）</param>
    /// <param name="progress">进度回调（描述、0~100）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task UpdateAsync(
        RepositoryConfig repository,
        IProgress<(string Stage, int Percent)>? progress = null,
        CancellationToken cancellationToken = default
    );
}
