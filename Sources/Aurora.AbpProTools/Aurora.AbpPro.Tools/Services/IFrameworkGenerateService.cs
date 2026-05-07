using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Aurora.AbpPro.Tools.Services;

/// <summary>
/// 框架生成服务：把「读取框架」得到的依赖图谱里所有项目复制到 Sources\AuroraAbpPro，
/// 并同步写入根解决方案 Aurora Application.slnx。
/// </summary>
public interface IFrameworkGenerateService
{
    /// <summary>
    /// 执行生成流水线（基于已有的 FrameworkReadResult）
    /// </summary>
    Task<FrameworkGenerateResult> GenerateAsync(
        FrameworkReadResult readResult,
        CancellationToken cancellationToken = default
    );
}

/// <summary>生成结果汇总</summary>
public class FrameworkGenerateResult
{
    /// <summary>是否整体成功</summary>
    public bool Success { get; set; }

    /// <summary>框架目标目录路径</summary>
    public string SlnxPath { get; set; } = string.Empty;

    /// <summary>已复制的项目数</summary>
    public int CopiedProjectCount { get; set; }

    /// <summary>已复制的文件数</summary>
    public int CopiedFileCount { get; set; }

    /// <summary>已重写引用的 csproj 数</summary>
    public int RewrittenCsprojCount { get; set; }

    /// <summary>详细日志（按顺序）</summary>
    public List<string> Messages { get; } = new();

    /// <summary>异常摘要（如果有）</summary>
    public string? Error { get; set; }
}
