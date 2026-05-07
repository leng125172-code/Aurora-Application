using System;
using System.Threading;
using System.Threading.Tasks;
using Aurora.AbpPro.Tools.Models;

namespace Aurora.AbpPro.Tools.Services;

/// <summary>
/// 框架生成服务：扫描已更新的框架目录中 Extract_* 子路径下的项目，依次执行 dotnet build
/// </summary>
public interface IFrameworkBuildService
{
    /// <summary>
    /// 对单个仓库执行框架生成（先 Debug 再 Release）
    /// </summary>
    /// <param name="repository">仓库配置（要求 FrameworkPath 已存在，Extract_* 已填充）</param>
    /// <param name="progress">进度回调（描述、0~100）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>处理结果统计</returns>
    Task<FrameworkBuildResult> BuildAsync(
        RepositoryConfig repository,
        IProgress<(string Stage, int Percent)>? progress = null,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// 框架生成结果统计
/// </summary>
public sealed class FrameworkBuildResult
{
    /// <summary>扫描到的可构建目标总数（解决方案或 csproj）</summary>
    public int TargetCount { get; set; }

    /// <summary>构建成功次数（按 Configuration × 目标数计）</summary>
    public int SucceededCount { get; set; }

    /// <summary>构建失败次数</summary>
    public int FailedCount { get; set; }
}
