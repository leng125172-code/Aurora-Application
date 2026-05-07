using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aurora.AbpPro.Tools.Models;
using Microsoft.Extensions.Logging;

namespace Aurora.AbpPro.Tools.Services;

/// <summary>
/// <see cref="IFrameworkBuildService"/> 默认实现：
/// 1) 扫描 <see cref="RepositoryConfig.FrameworkPath"/> 下四个 Extract_* 子目录；
/// 2) 优先以解决方案 (.slnx/.sln) 为构建单元，否则按 *.csproj 逐个构建；
/// 3) 依次执行 Debug 与 Release 两个 Configuration。
/// </summary>
public class FrameworkBuildService : IFrameworkBuildService
{
    private readonly ILogger<FrameworkBuildService> _logger;

    /// <summary>
    /// 构建配置列表（顺序敏感：先 Debug 再 Release）
    /// </summary>
    private static readonly string[] BuildConfigurations = { "Debug", "Release" };

    /// <summary>
    /// 子进程标准输出/错误所用编码：使用当前系统 OEM 代码页（中文 Windows 默认 936），
    /// 与 dotnet CLI 实际写出的字节保持一致，避免日志中文乱码。
    /// </summary>
    private static readonly Encoding ConsoleEncoding = ResolveConsoleEncoding();

    private static Encoding ResolveConsoleEncoding()
    {
        // 注册 CodePagesEncodingProvider 以支持 GB2312/GBK/CP936 等非 UTF-8 编码
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            return Encoding.GetEncoding(
                System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage
            );
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    public FrameworkBuildService(ILogger<FrameworkBuildService> logger)
    {
        _logger = logger;
    }

    public async Task<FrameworkBuildResult> BuildAsync(
        RepositoryConfig repository,
        IProgress<(string Stage, int Percent)>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        if (repository == null)
        {
            throw new ArgumentNullException(nameof(repository));
        }
        if (string.IsNullOrWhiteSpace(repository.FrameworkPath))
        {
            throw new InvalidOperationException("FrameworkPath 为空，无法定位框架目录");
        }
        if (!Directory.Exists(repository.FrameworkPath))
        {
            throw new DirectoryNotFoundException(
                $"框架目录不存在，请先执行「更新框架」：{repository.FrameworkPath}"
            );
        }

        var result = new FrameworkBuildResult();

        // 收集需要构建的目标：优先解决方案，否则收集子目录下所有 csproj
        var targets = CollectBuildTargets(repository);
        result.TargetCount = targets.Count;
        if (targets.Count == 0)
        {
            _logger.LogWarning(
                "未在 {Path} 的 Extract_* 子目录下找到任何可构建项目",
                repository.FrameworkPath
            );
            progress?.Report(("未找到可构建目标", 100));
            return result;
        }

        _logger.LogInformation(
            "框架生成开始：{Owner}/{Repo}，共 {Count} 个构建目标",
            repository.Owner,
            repository.RepositoryId,
            targets.Count
        );

        var totalSteps = targets.Count * BuildConfigurations.Length;
        var stepIndex = 0;
        foreach (var configuration in BuildConfigurations)
        {
            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                stepIndex++;
                var percent = (int)(stepIndex * 100.0 / totalSteps);
                var displayName = Path.GetFileName(target);
                progress?.Report(($"{configuration}：{displayName}", percent));
                _logger.LogInformation("[{Config}] 构建：{Target}", configuration, target);
                var ok = await RunDotnetBuildAsync(target, configuration, cancellationToken);
                if (ok)
                {
                    result.SucceededCount++;
                }
                else
                {
                    result.FailedCount++;
                }
            }
        }

        progress?.Report(("完成", 100));
        return result;
    }

    /// <summary>
    /// 收集构建目标：
    /// 取 Extract_* 子目录的「公共上级路径」（例如 aspnet-core/frameworks → aspnet-core），
    /// 然后递归枚举该上级目录下所有 .slnx / .sln 文件作为构建目标。
    /// 这样可避免单独构建子项目时因解决方案级依赖缺失而失败。
    /// </summary>
    private List<string> CollectBuildTargets(RepositoryConfig repository)
    {
        var subPaths = new[]
        {
            repository.Extract_FrameworkPath,
            repository.Extract_GatewaysPath,
            repository.Extract_ServicePath,
            repository.Extract_ModulePath,
        };

        // 计算各子路径的上级目录（相对 FrameworkPath），并去重
        var scanRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sub in subPaths)
        {
            if (string.IsNullOrWhiteSpace(sub))
            {
                continue;
            }
            // 统一分隔符后取上级目录；若已是顶级（无父级）则直接使用其本身
            var normalized = sub.Replace('\\', '/').Trim('/');
            var parent = Path.GetDirectoryName(
                normalized.Replace('/', Path.DirectorySeparatorChar)
            );
            var rootRelative = string.IsNullOrEmpty(parent) ? normalized : parent;
            var absolute = Path.GetFullPath(Path.Combine(repository.FrameworkPath, rootRelative));
            if (Directory.Exists(absolute))
            {
                scanRoots.Add(absolute);
            }
            else
            {
                _logger.LogInformation("扫描根目录不存在，跳过：{Path}", absolute);
            }
        }

        var targets = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in scanRoots)
        {
            var solutions = Directory
                .EnumerateFiles(root, "*.slnx", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(root, "*.sln", SearchOption.AllDirectories));
            foreach (var sln in solutions)
            {
                if (visited.Add(sln))
                {
                    targets.Add(sln);
                }
            }
        }

        return targets;
    }

    /// <summary>
    /// 调用 dotnet build 构建指定目标（解决方案或 csproj），返回是否成功
    /// </summary>
    private async Task<bool> RunDotnetBuildAsync(
        string target,
        string configuration,
        CancellationToken cancellationToken
    )
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetDirectoryName(target) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // 与中文 Windows 控制台默认 OEM 编码（CP936）对齐，防止日志乱码
            StandardOutputEncoding = ConsoleEncoding,
            StandardErrorEncoding = ConsoleEncoding,
        };
        psi.ArgumentList.Add("build");
        psi.ArgumentList.Add(target);
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(configuration);
        psi.ArgumentList.Add("-nologo");
        psi.ArgumentList.Add("-v:m");

        try
        {
            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    _logger.LogDebug("[build] {Line}", e.Data);
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    _logger.LogWarning("[build][err] {Line}", e.Data);
                }
            };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
            {
                _logger.LogError(
                    "[{Config}] 构建失败 (ExitCode={Code})：{Target}",
                    configuration,
                    process.ExitCode,
                    target
                );
                return false;
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Config}] 构建异常：{Target}", configuration, target);
            return false;
        }
    }
}
