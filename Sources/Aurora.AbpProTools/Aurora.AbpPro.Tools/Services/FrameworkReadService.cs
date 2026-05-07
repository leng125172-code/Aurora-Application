using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Aurora.AbpPro.Tools.Models;
using Aurora.AbpPro.Tools.ViewModels;
using Microsoft.Extensions.Logging;

namespace Aurora.AbpPro.Tools.Services;

/// <summary>
/// 框架读取服务实现：
/// 以业务应用项目中的 PackageReference/ProjectReference 作为起点，
/// 在本地框架源码目录中按“包名或项目名 = csproj 文件名”规则递归匹配项目。
/// </summary>
public class FrameworkReadService : IFrameworkReadService
{
    private readonly ILogger<FrameworkReadService> _logger;
    private readonly ReadonlyConfig _readonlyConfig;

    /// <summary>
    /// 测试项目过滤标记（项目名包含其一即视为测试项目，排除）
    /// </summary>
    private static readonly string[] TestMarkers = new[] { ".Tests", ".Test" };

    public FrameworkReadService(ILogger<FrameworkReadService> logger, ReadonlyConfig readonlyConfig)
    {
        _logger = logger;
        _readonlyConfig = readonlyConfig;
    }

    public Task<FrameworkReadResult> ReadAsync(
        IReadOnlyList<RepositoryReleaseItem> items,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = new FrameworkReadResult();

                if (!Directory.Exists(AuroraGenerateConfig.ApplicationProjectRoot))
                {
                    throw new InvalidOperationException(
                        $"未找到业务应用项目目录：{AuroraGenerateConfig.ApplicationProjectRoot}"
                    );
                }
                if (!Directory.Exists(AuroraGenerateConfig.FrameworkSourceRoot))
                {
                    throw new InvalidOperationException(
                        $"未找到本地框架源码目录：{AuroraGenerateConfig.FrameworkSourceRoot}"
                    );
                }

                WriteProcessLog($"正在扫描：{AuroraGenerateConfig.ApplicationProjectRoot}");
                WriteProcessLog($"正在扫描：{AuroraGenerateConfig.FrameworkSourceRoot}");

                // 1) 建立本地框架源码的「项目名 → CsProjectInfo」索引。
                var globalIndex = new Dictionary<string, CsProjectInfo>(
                    StringComparer.OrdinalIgnoreCase
                );
                AddIndex(globalIndex, AuroraGenerateConfig.FrameworkSourceRoot, "local-framework");
                _logger.LogInformation("本地框架源码已索引项目数：{Count}", globalIndex.Count);

                cancellationToken.ThrowIfCancellationRequested();

                // 2) 读取业务应用全部项目的 PackageReference/ProjectReference，作为第一次匹配起点。
                ScanApplicationPackageSeeds(globalIndex, result);
                WriteProcessLog(
                    $"第 1 轮匹配，找到 {result.SeedProjects.Count} 个新项目{FormatProjectKinds(result.SeedProjects)}"
                );
                _logger.LogInformation(
                    "业务应用引用匹配种子项目数：{Count}",
                    result.SeedProjects.Count
                );

                // 3) 循环读取结果 A/B/C... 的 PackageReference，递归匹配到所有需要拷贝的 csproj。
                ExpandPackageGraph(globalIndex, result, cancellationToken);
                WriteProcessLog($"去重后总数：{result.AllProjects.Count}");
                _logger.LogInformation(
                    "框架依赖规模：项目节点={Nodes} 边={Edges} 包映射={Map} 未匹配包={Unresolved}",
                    result.AllProjects.Count,
                    result.Edges.Values.Sum(v => v.Count),
                    result.PackagePathMap.Count,
                    result.UnresolvedPackages.Count
                );

                // 4) 渲染依赖树并写日志。
                result.TreeText = RenderTree(result);
                _logger.LogInformation("依赖树：\n{Tree}", result.TreeText);

                return result;
            },
            cancellationToken
        );
    }

    // ============== 1) 全局索引构建 ==============

    private static void AddIndex(
        Dictionary<string, CsProjectInfo> index,
        string root,
        string sourceRepo
    )
    {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            return;
        }
        foreach (
            var csproj in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
        )
        {
            var name = Path.GetFileNameWithoutExtension(csproj);
            // 跳过测试项目，避免污染索引
            if (IsTestProject(name))
            {
                continue;
            }
            // 同名仅保留首个出现（aspnet-core 与 abpframework 极少冲突；冲突时优先级靠调用顺序保证）
            if (index.ContainsKey(name))
            {
                continue;
            }
            index[name] = new CsProjectInfo
            {
                Name = name,
                FilePath = csproj,
                SourceRepo = sourceRepo,
                IsWpfProject = IsWpfProject(csproj),
                IsHostProject = IsHostProject(csproj),
            };
        }
    }

    // ============== 2) 种子项目扫描（来自业务应用 PackageReference） ==============

    /// <summary>
    /// 读取业务应用项目中的所有 PackageReference/ProjectReference，并在框架源码索引中匹配第一层项目。
    /// </summary>
    private static void ScanApplicationPackageSeeds(
        Dictionary<string, CsProjectInfo> globalIndex,
        FrameworkReadResult result
    )
    {
        static void TryAddSeed(
            string referenceName,
            Dictionary<string, CsProjectInfo> index,
            FrameworkReadResult readResult
        )
        {
            if (string.IsNullOrWhiteSpace(referenceName))
            {
                return;
            }

            if (!index.TryGetValue(referenceName, out var hit))
            {
                return;
            }

            if (!readResult.PackagePathMap.ContainsKey(referenceName))
            {
                readResult.PackagePathMap[referenceName] = hit.FilePath;
            }

            if (!readResult.AllProjects.ContainsKey(hit.Name))
            {
                hit.Depth = 0;
                readResult.SeedProjects.Add(hit);
                readResult.AllProjects[hit.Name] = hit;
            }
        }

        foreach (
            var csprojPath in Directory.EnumerateFiles(
                AuroraGenerateConfig.ApplicationProjectRoot,
                "*.csproj",
                SearchOption.AllDirectories
            )
        )
        {
            foreach (var packageReference in EnumeratePackageReferences(csprojPath))
            {
                packageReference.ProjectPath = csprojPath;
                if (globalIndex.TryGetValue(packageReference.Name, out var hit))
                {
                    TryAddSeed(packageReference.Name, globalIndex, result);
                }
                else if (!result.UnresolvedPackages.ContainsKey(packageReference.Name))
                {
                    result.UnresolvedPackages[packageReference.Name] =
                        packageReference.Version ?? string.Empty;
                }
            }

            // 兼容已转换为 ProjectReference 的业务项目：从引用项目文件名提取种子。
            foreach (var projectReferenceName in EnumerateProjectReferenceNames(csprojPath))
            {
                TryAddSeed(projectReferenceName, globalIndex, result);
            }
        }
    }

    // ============== 3) PackageReference 递归展开 ==============

    /// <summary>
    /// 队列中每个项目都会读取它的 PackageReference，把所有能在框架源码索引中找到对应 csproj 的引用纳入图谱并继续递归。
    /// </summary>
    private static void ExpandPackageGraph(
        Dictionary<string, CsProjectInfo> globalIndex,
        FrameworkReadResult result,
        CancellationToken cancellationToken
    )
    {
        var currentRound = result.SeedProjects.ToList();
        int round = 2;

        while (currentRound.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nextRound = new List<CsProjectInfo>();
            foreach (var current in currentRound)
            {
                var children = result.Edges.GetValueOrDefault(current.Name) ?? new List<string>();
                foreach (var pkg in EnumeratePackageReferences(current.FilePath))
                {
                    if (globalIndex.TryGetValue(pkg.Name, out var hit))
                    {
                        if (!result.PackagePathMap.ContainsKey(pkg.Name))
                        {
                            result.PackagePathMap[pkg.Name] = hit.FilePath;
                        }
                        if (AddNode(result, hit, current, children))
                        {
                            nextRound.Add(hit);
                        }
                    }
                    else if (!result.UnresolvedPackages.ContainsKey(pkg.Name))
                    {
                        result.UnresolvedPackages[pkg.Name] = pkg.Version ?? string.Empty;
                    }
                }

                if (children.Count > 0)
                {
                    result.Edges[current.Name] = children;
                }
            }
            WriteProcessLog(
                $"第 {round} 轮匹配，找到 {nextRound.Count} 个新项目{FormatProjectKinds(nextRound)}"
            );
            if (nextRound.Count == 0)
            {
                break;
            }
            currentRound = nextRound;
            round++;
        }
    }

    /// <summary>
    /// 把发现的依赖项目纳入图谱：去重、登记到 AllProjects、设置 Depth、加入边集合、入队继续展开
    /// </summary>
    private static bool AddNode(
        FrameworkReadResult result,
        CsProjectInfo dep,
        CsProjectInfo parent,
        List<string> parentChildren
    )
    {
        if (!parentChildren.Contains(dep.Name, StringComparer.OrdinalIgnoreCase))
        {
            parentChildren.Add(dep.Name);
        }
        if (result.AllProjects.ContainsKey(dep.Name))
        {
            return false;
        }
        dep.Depth = parent.Depth + 1;
        result.AllProjects[dep.Name] = dep;
        return true;
    }

    // ============== 4) 渲染 ==============

    /// <summary>
    /// 渲染依赖树：先按"模板 → 直接子项目"展开两层，再单独列出全部已发现项目（按来源仓库分组）和未匹配包
    /// </summary>
    private static string RenderTree(FrameworkReadResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("框架依赖图（业务应用 PackageReference 出发，沿 PackageReference 递归展开）");
        sb.AppendLine($"├── 种子项目 [{result.SeedProjects.Count}]");
        foreach (var t in result.SeedProjects)
        {
            sb.AppendLine($"│   ├── {t.Name}");
            // 仅显示种子的直接子节点，避免日志爆炸
            if (result.Edges.TryGetValue(t.Name, out var firstLevel))
            {
                for (int i = 0; i < firstLevel.Count; i++)
                {
                    var isLast = i == firstLevel.Count - 1;
                    var child = result.AllProjects.GetValueOrDefault(firstLevel[i]);
                    var repo = child?.SourceRepo ?? "?";
                    sb.AppendLine($"│   │   {(isLast ? "└──" : "├──")} [{repo}] {firstLevel[i]}");
                }
            }
        }

        // 按来源仓库分组列出全部项目
        var groups = result
            .AllProjects.Values.GroupBy(p => p.SourceRepo)
            .OrderBy(g => g.Key, StringComparer.Ordinal);
        sb.AppendLine($"├── 全部已串到的项目 [{result.AllProjects.Count}]");
        foreach (var g in groups)
        {
            sb.AppendLine($"│   ├── [{g.Key}] {g.Count()}");
            foreach (var p in g.OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                sb.AppendLine($"│   │   ├── {p.Name}  ({p.FilePath})");
            }
        }

        // 包路径映射
        sb.AppendLine($"├── 包→csproj 映射 [{result.PackagePathMap.Count}]");
        foreach (var kv in result.PackagePathMap.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            sb.AppendLine($"│   ├── {kv.Key} → {kv.Value}");
        }

        // 未匹配的外部包
        sb.AppendLine(
            $"└── 外部包（未在全局依赖项目目录中找到源码） [{result.UnresolvedPackages.Count}]"
        );
        foreach (var kv in result.UnresolvedPackages.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            sb.AppendLine($"    ├── {kv.Key} {kv.Value}");
        }
        return sb.ToString();
    }

    // ============== 工具方法 ==============

    private static bool IsTestProject(string name)
    {
        foreach (var marker in TestMarkers)
        {
            if (name.EndsWith(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 判断项目是否包含 WPF 资源或 WPF SDK 标记。
    /// </summary>
    private static bool IsWpfProject(string csprojPath)
    {
        try
        {
            var projectDirectory = Path.GetDirectoryName(csprojPath);
            if (string.IsNullOrWhiteSpace(projectDirectory))
            {
                return false;
            }
            var projectText = File.ReadAllText(csprojPath);
            return projectText.Contains(
                    "Microsoft.NET.Sdk.WindowsDesktop",
                    StringComparison.OrdinalIgnoreCase
                )
                || projectText.Contains("<UseWPF>true</UseWPF>", StringComparison.OrdinalIgnoreCase)
                || Directory
                    .EnumerateFiles(projectDirectory, "*.xaml", SearchOption.AllDirectories)
                    .Any();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 判断项目是否包含 Host 启动或配置特征。
    /// </summary>
    private static bool IsHostProject(string csprojPath)
    {
        try
        {
            var projectDirectory = Path.GetDirectoryName(csprojPath);
            if (string.IsNullOrWhiteSpace(projectDirectory))
            {
                return false;
            }
            var projectText = File.ReadAllText(csprojPath);
            return projectText.Contains(
                    "Microsoft.Extensions.Hosting",
                    StringComparison.OrdinalIgnoreCase
                )
                || File.Exists(Path.Combine(projectDirectory, "Program.cs"))
                || File.Exists(Path.Combine(projectDirectory, "Startup.cs"))
                || Directory
                    .EnumerateFiles(
                        projectDirectory,
                        "appsettings*.json",
                        SearchOption.TopDirectoryOnly
                    )
                    .Any();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 格式化项目类型标识。
    /// </summary>
    private static string FormatProjectKinds(IEnumerable<CsProjectInfo> projects)
    {
        var projectList = projects.ToList();
        var wpfCount = projectList.Count(p => p.IsWpfProject);
        var hostCount = projectList.Count(p => p.IsHostProject);
        return $"（WPF：{wpfCount}，Host：{hostCount}）";
    }

    /// <summary>
    /// 写入流程日志。
    /// </summary>
    private static void WriteProcessLog(string message)
    {
        Console.WriteLine(message);
    }

    /// <summary>
    /// 读取 csproj 中所有 PackageReference 节点 (Name, Version)
    /// </summary>
    private static IEnumerable<PackageRefInfo> EnumeratePackageReferences(string csprojPath)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Load(csprojPath);
        }
        catch
        {
            yield break;
        }
        foreach (var node in doc.Descendants().Where(e => e.Name.LocalName == "PackageReference"))
        {
            var name = node.Attribute("Include")?.Value ?? node.Attribute("Update")?.Value;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }
            var version =
                node.Attribute("Version")?.Value
                ?? node.Element(node.Name.Namespace + "Version")?.Value
                ?? string.Empty;
            yield return new PackageRefInfo { Name = name, Version = version };
        }
    }

    /// <summary>
    /// 读取 csproj 中所有 ProjectReference 节点，返回引用项目名（不带扩展名）。
    /// </summary>
    private static IEnumerable<string> EnumerateProjectReferenceNames(string csprojPath)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Load(csprojPath);
        }
        catch
        {
            yield break;
        }

        var csprojDirectory = Path.GetDirectoryName(csprojPath);
        if (string.IsNullOrWhiteSpace(csprojDirectory))
        {
            yield break;
        }

        foreach (var node in doc.Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
        {
            var include = node.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(include))
            {
                continue;
            }

            string resolvedPath;
            try
            {
                resolvedPath = Path.GetFullPath(Path.Combine(csprojDirectory, include));
            }
            catch
            {
                continue;
            }

            var projectName = Path.GetFileNameWithoutExtension(resolvedPath);
            if (!string.IsNullOrWhiteSpace(projectName))
            {
                yield return projectName;
            }
        }
    }
}
