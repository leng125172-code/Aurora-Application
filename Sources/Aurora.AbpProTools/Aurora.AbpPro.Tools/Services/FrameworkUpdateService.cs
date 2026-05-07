using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Aurora.AbpPro.Tools.Models;
using Microsoft.Extensions.Logging;

namespace Aurora.AbpPro.Tools.Services;

/// <summary>
/// <see cref="IFrameworkUpdateService"/> 默认实现：
/// 1) 自动定位解压目录中的实际源码根（通常为 GitHub 源码包里的 {Owner}-{Repo}-{Hash} 子目录）；
/// 2) 复制到 <see cref="RepositoryConfig.FrameworkPath"/>（先清空目标目录）；
/// 3) 按 ReplaceContent Key 升序依次执行「目录/文件名」普通字符串替换；
/// 4) 按 ReplaceContent Key 升序依次执行「文本内容」正则替换（区分大小写 + 全字匹配）。
/// </summary>
public class FrameworkUpdateService : IFrameworkUpdateService
{
    private readonly ILogger<FrameworkUpdateService> _logger;

    /// <summary>
    /// 文本类文件扩展名白名单（其余视作二进制文件，跳过内容替换）。
    /// 之所以用「白名单」而非「黑名单」：可显著降低误改二进制文件（如 .dll、.png）的风险。
    /// </summary>
    private static readonly HashSet<string> TextFileExtensions = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ".cs",
        ".csproj",
        ".sln",
        ".slnx",
        ".props",
        ".targets",
        ".json",
        ".xml",
        ".config",
        ".yml",
        ".yaml",
        ".md",
        ".txt",
        ".razor",
        ".cshtml",
        ".html",
        ".htm",
        ".css",
        ".scss",
        ".less",
        ".js",
        ".ts",
        ".tsx",
        ".jsx",
        ".vue",
        ".ps1",
        ".bat",
        ".cmd",
        ".sh",
        ".py",
        ".editorconfig",
        ".gitignore",
        ".gitattributes",
        ".resx",
        ".axaml",
        ".xaml",
        ".vbproj",
        ".fsproj",
        ".http",
        ".env",
        ".dockerfile",
    };

    /// <summary>
    /// 无扩展名但属于文本的常见文件名（按文件名整体匹配）。
    /// </summary>
    private static readonly HashSet<string> TextFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dockerfile",
        "LICENSE",
        "README",
        "CHANGELOG",
        "NuGet.Config",
    };

    /// <summary>
    /// 图片资源扩展名集合（用于将项目内所有图片统一替换为 Assets/AuroraApplication.png）
    /// </summary>
    private static readonly HashSet<string> ImageFileExtensions = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".gif",
        ".ico",
        ".webp",
        ".svg",
    };

    public FrameworkUpdateService(ILogger<FrameworkUpdateService> logger)
    {
        _logger = logger;
    }

    public async Task UpdateAsync(
        RepositoryConfig repository,
        IProgress<(string Stage, int Percent)>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        if (repository == null)
        {
            throw new ArgumentNullException(nameof(repository));
        }
        if (string.IsNullOrWhiteSpace(repository.ExtractDirectory))
        {
            throw new InvalidOperationException("ExtractDirectory 为空，请先完成下载/解压");
        }
        if (string.IsNullOrWhiteSpace(repository.FrameworkPath))
        {
            throw new InvalidOperationException("FrameworkPath 为空，无法确定目标目录");
        }
        if (!Directory.Exists(repository.ExtractDirectory))
        {
            throw new DirectoryNotFoundException($"解压目录不存在：{repository.ExtractDirectory}");
        }

        // 解析替换规则：Dictionary<int,string> { 1,"Lion,Aurora" } → (key=1, old=Lion, new=Aurora)
        var rules = ParseReplaceRules(repository.ReplaceContent);
        _logger.LogInformation(
            "框架更新开始：{Owner}/{Repo} → {Target}（{Count} 条替换规则）",
            repository.Owner,
            repository.RepositoryId,
            repository.FrameworkPath,
            rules.Count
        );

        // 1) 定位真正的源码根目录（GitHub 源码包通常套了一层 {Owner}-{Repo}-{Hash}）
        var sourceRoot = ResolveSourceRoot(repository.ExtractDirectory);
        _logger.LogInformation("源码根目录：{Root}", sourceRoot);

        await Task.Run(
            () =>
            {
                // 2) 清空目标目录后复制
                progress?.Report(("准备目标目录", 5));
                CleanDirectory(repository.FrameworkPath);

                progress?.Report(("正在复制源码", 15));
                CopyDirectory(
                    sourceRoot,
                    repository.FrameworkPath,
                    repository.ExcludeFolders,
                    repository.ExcludeFiles,
                    cancellationToken
                );
                _logger.LogInformation("源码复制完成 → {Target}", repository.FrameworkPath);

                // 3) 重命名目录与文件（自下而上，避免父目录改名后子路径失效）
                progress?.Report(("重命名目录与文件", 50));
                RenameDirectoriesAndFiles(repository.FrameworkPath, rules, cancellationToken);
                _logger.LogInformation("目录/文件名替换完成");

                // 4) 文件内容替换（正则全字匹配 + 大小写敏感）
                progress?.Report(("替换文件内容", 70));
                ReplaceFileContents(repository.FrameworkPath, rules, cancellationToken);
                _logger.LogInformation("文件内容替换完成");

                // 5) 为每个 csproj 注入 Debug/Release 输出路径属性组（统一输出到 Builds 目录）
                progress?.Report(("注入项目输出路径", 80));
                InjectOutputPathToProjects(repository.FrameworkPath, cancellationToken);
                _logger.LogInformation("csproj 输出路径注入完成");

                // 6) 图标与图片资源统一替换
                progress?.Report(("替换图标与图片资源", 88));
                ReplaceIconsAndImages(repository, cancellationToken);
                _logger.LogInformation("图标与图片资源替换完成");

                // 7) 从所有解决方案文件中移除 ExcludeProj 列出的项目
                progress?.Report(("清理解决方案中的排除项目", 95));
                RemoveExcludedProjectsFromSolutions(repository, rules, cancellationToken);
                _logger.LogInformation("解决方案排除项目清理完成");

                // 8) 兜底：移除 sln/slnx 中磁盘上已不存在的项目引用，避免 dotnet 报 MSB3202
                progress?.Report(("清理解决方案中的悬挂引用", 96));
                RemoveDanglingProjectsFromSolutions(repository.FrameworkPath, cancellationToken);
                _logger.LogInformation("解决方案悬挂引用清理完成");

                // 9) 把 aspnet-core 根的 *.targets 同步到每个 sln/slnx 所在目录，保证缺失时也能找到
                progress?.Report(("同步解决方案目录的 targets 文件", 98));
                SyncTargetsToSolutionDirectories(repository, cancellationToken);
                _logger.LogInformation("解决方案目录 targets 同步完成");

                // 10) 确保每个 csproj 同级目录都存在 icon.png，避免打包/构建时找不到资源
                progress?.Report(("同步项目目录的图标资源", 99));
                EnsureIconForEveryProject(repository, cancellationToken);
                _logger.LogInformation("项目目录图标同步完成");

                progress?.Report((("完成", 100)));
            },
            cancellationToken
        );
    }

    #region 内部实现

    /// <summary>
    /// 单条替换规则
    /// </summary>
    private readonly record struct ReplaceRule(int Key, string OldValue, string NewValue);

    /// <summary>
    /// 将 Dictionary&lt;int,string&gt; 形式的配置解析为有序规则列表（按 Key 升序）。
    /// 值格式："原始文本,替换后文本"，仅按第一个英文逗号分割（允许新值中包含逗号）。
    /// </summary>
    private static List<ReplaceRule> ParseReplaceRules(IDictionary<int, string>? replaceContent)
    {
        var rules = new List<ReplaceRule>();
        if (replaceContent == null || replaceContent.Count == 0)
        {
            return rules;
        }

        foreach (var pair in replaceContent.OrderBy(p => p.Key))
        {
            if (string.IsNullOrEmpty(pair.Value))
            {
                continue;
            }
            var separatorIndex = pair.Value.IndexOf(',');
            if (separatorIndex <= 0 || separatorIndex >= pair.Value.Length - 1)
            {
                // 缺少逗号或位置异常，跳过
                continue;
            }
            var oldValue = pair.Value[..separatorIndex];
            var newValue = pair.Value[(separatorIndex + 1)..];
            if (string.IsNullOrEmpty(oldValue))
            {
                continue;
            }
            rules.Add(new ReplaceRule(pair.Key, oldValue, newValue));
        }
        return rules;
    }

    /// <summary>
    /// 找到真正的源码根：若 extractDirectory 仅含一个子目录，则返回该子目录；否则返回自身。
    /// </summary>
    private static string ResolveSourceRoot(string extractDirectory)
    {
        var subDirs = Directory.GetDirectories(extractDirectory);
        var files = Directory.GetFiles(extractDirectory);
        if (subDirs.Length == 1 && files.Length == 0)
        {
            return subDirs[0];
        }
        return extractDirectory;
    }

    /// <summary>
    /// 清空目标目录（存在则递归删除后重建），带只读属性清除 + 多次重试，规避 Windows 文件占用
    /// </summary>
    private void CleanDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                ResetReadOnlyAttributes(path);
                DeleteDirectoryWithRetry(path);
            }
            Directory.CreateDirectory(path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清空目标目录失败：{Path}", path);
            throw;
        }
    }

    /// <summary>
    /// 健壮的递归删除：指数退避重试 + GC 释放残留句柄
    /// </summary>
    private void DeleteDirectoryWithRetry(string path, int maxAttempts = 5)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                if (attempt == maxAttempts)
                {
                    throw;
                }
                _logger.LogWarning(
                    "删除目录被占用，第 {Attempt}/{Max} 次重试：{Path}",
                    attempt,
                    maxAttempts,
                    path
                );
                Thread.Sleep(200 * (int)Math.Pow(2, attempt - 1));
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
    }

    /// <summary>
    /// 递归去除目录下所有文件的只读属性
    /// </summary>
    private static void ResetReadOnlyAttributes(string path)
    {
        var dir = new DirectoryInfo(path);
        foreach (var file in dir.GetFiles("*", SearchOption.AllDirectories))
        {
            if (file.IsReadOnly)
            {
                file.IsReadOnly = false;
            }
        }
    }

    /// <summary>
    /// 递归复制目录，支持按名称排除子文件夹，按文件名（含通配符 *?）排除文件
    /// </summary>
    private static void CopyDirectory(
        string sourceDir,
        string targetDir,
        IReadOnlyList<string>? excludeFolders,
        IReadOnlyList<string>? excludeFiles,
        CancellationToken cancellationToken
    )
    {
        var excludedFolderSet = new HashSet<string>(
            excludeFolders ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase
        );
        var fileMatchers =
            excludeFiles
                ?.Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(BuildWildcardRegex)
                .ToList()
            ?? new List<Regex>();

        Directory.CreateDirectory(targetDir);
        CopyDirectoryRecursive(
            sourceDir,
            targetDir,
            excludedFolderSet,
            fileMatchers,
            cancellationToken
        );
    }

    /// <summary>
    /// CopyDirectory 的递归实现：按层遍历，遇到排除目录直接跳过
    /// </summary>
    private static void CopyDirectoryRecursive(
        string sourceDir,
        string targetDir,
        HashSet<string> excludedFolderSet,
        IReadOnlyList<Regex> fileMatchers,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(targetDir);

        // 复制当前层文件
        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(file);
            if (IsFileExcluded(name, fileMatchers))
            {
                continue;
            }
            File.Copy(file, Path.Combine(targetDir, name), overwrite: true);
        }

        // 递归子目录
        foreach (var dir in Directory.EnumerateDirectories(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(dir);
            if (excludedFolderSet.Contains(name))
            {
                continue;
            }
            CopyDirectoryRecursive(
                dir,
                Path.Combine(targetDir, name),
                excludedFolderSet,
                fileMatchers,
                cancellationToken
            );
        }
    }

    /// <summary>
    /// 判断文件名是否匹配任一排除规则（大小写不敏感，贴近 Windows 习惯）
    /// </summary>
    private static bool IsFileExcluded(string fileName, IReadOnlyList<Regex> matchers)
    {
        for (var i = 0; i < matchers.Count; i++)
        {
            if (matchers[i].IsMatch(fileName))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 将含 * ? 的通配符转为整体匹配的正则（仅用于文件名）
    /// </summary>
    private static Regex BuildWildcardRegex(string pattern)
    {
        var escaped = Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".");
        return new Regex(
            $"^{escaped}$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
        );
    }

    /// <summary>
    /// 重命名目录和文件：先文件后目录（目录按深度倒序），保证父路径改名前子项已完成
    /// </summary>
    private void RenameDirectoriesAndFiles(
        string root,
        IReadOnlyList<ReplaceRule> rules,
        CancellationToken cancellationToken
    )
    {
        if (rules.Count == 0)
        {
            return;
        }

        // 先重命名所有文件
        var files = Directory
            .GetFiles(root, "*", SearchOption.AllDirectories)
            .OrderByDescending(p => p.Length)
            .ToList();
        foreach (var filePath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var directory = Path.GetDirectoryName(filePath)!;
                var fileName = Path.GetFileName(filePath);
                var newFileName = ApplyPlainReplace(fileName, rules);
                if (!string.Equals(fileName, newFileName, StringComparison.Ordinal))
                {
                    var newPath = Path.Combine(directory, newFileName);
                    if (File.Exists(newPath))
                    {
                        File.Delete(newPath);
                    }
                    File.Move(filePath, newPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "重命名文件失败：{Path}", filePath);
            }
        }

        // 再重命名目录（按路径深度从深到浅）
        var directories = Directory
            .GetDirectories(root, "*", SearchOption.AllDirectories)
            .OrderByDescending(p => p.Count(c => c == Path.DirectorySeparatorChar))
            .ToList();
        foreach (var dirPath in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!Directory.Exists(dirPath))
                {
                    continue;
                }
                var parent = Path.GetDirectoryName(dirPath)!;
                var name = Path.GetFileName(dirPath);
                var newName = ApplyPlainReplace(name, rules);
                if (!string.Equals(name, newName, StringComparison.Ordinal))
                {
                    var newPath = Path.Combine(parent, newName);
                    if (Directory.Exists(newPath))
                    {
                        // 目标已存在：合并到目标后删除当前目录
                        MergeDirectory(dirPath, newPath);
                        Directory.Delete(dirPath, recursive: true);
                    }
                    else
                    {
                        Directory.Move(dirPath, newPath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "重命名目录失败：{Path}", dirPath);
            }
        }
    }

    /// <summary>
    /// 普通字符串替换（按 Key 升序，等价 string.Replace 多次）
    /// </summary>
    private static string ApplyPlainReplace(string input, IReadOnlyList<ReplaceRule> rules)
    {
        var result = input;
        foreach (var rule in rules)
        {
            result = result.Replace(rule.OldValue, rule.NewValue, StringComparison.Ordinal);
        }
        return result;
    }

    /// <summary>
    /// 将 source 目录内容合并到 target 目录（同名文件覆盖，子目录递归）
    /// </summary>
    private static void MergeDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            var dest = Path.Combine(target, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
        }
        foreach (var dir in Directory.GetDirectories(source))
        {
            MergeDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        }
    }

    /// <summary>
    /// 从所有解决方案文件（.sln / .slnx）中移除 <see cref="RepositoryConfig.ExcludeProj"/> 列出的项目。
    /// 配置中的项目名为「替换前」名称（如 Lion.AbpPro.Cli.csproj），这里会先按 ReplaceContent 规则
    /// 转换为「替换后」名称再匹配。
    /// </summary>
    private void RemoveExcludedProjectsFromSolutions(
        RepositoryConfig repository,
        IReadOnlyList<ReplaceRule> rules,
        CancellationToken cancellationToken
    )
    {
        if (repository.ExcludeProj == null || repository.ExcludeProj.Count == 0)
        {
            return;
        }

        // 计算「替换后」的项目文件名集合（仅文件名，不含路径）
        var excludedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in repository.ExcludeProj)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }
            excludedFileNames.Add(Path.GetFileName(name));
            // 同时加入按规则替换后的名字
            excludedFileNames.Add(Path.GetFileName(ApplyPlainReplace(name, rules)));
        }

        // 同步把项目从磁盘删除（避免悬挂的 csproj 与目录），再清理解决方案
        DeleteExcludedProjectFiles(repository.FrameworkPath, excludedFileNames, cancellationToken);

        var solutionFiles = Directory
            .EnumerateFiles(repository.FrameworkPath, "*.sln", SearchOption.AllDirectories)
            .Concat(
                Directory.EnumerateFiles(
                    repository.FrameworkPath,
                    "*.slnx",
                    SearchOption.AllDirectories
                )
            )
            .ToList();

        foreach (var solution in solutionFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var ext = Path.GetExtension(solution);
                if (string.Equals(ext, ".slnx", StringComparison.OrdinalIgnoreCase))
                {
                    RemoveProjectsFromSlnx(solution, excludedFileNames);
                }
                else
                {
                    RemoveProjectsFromSln(solution, excludedFileNames);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "处理解决方案文件失败：{Path}", solution);
            }
        }
    }

    /// <summary>
    /// 物理删除排除项目对应的 csproj 文件及其所在目录（若同名目录仅含该 csproj）
    /// </summary>
    private void DeleteExcludedProjectFiles(
        string root,
        HashSet<string> excludedFileNames,
        CancellationToken cancellationToken
    )
    {
        foreach (
            var csproj in Directory
                .EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
                .ToList()
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(csproj);
            if (!excludedFileNames.Contains(name))
            {
                continue;
            }
            try
            {
                var dir = Path.GetDirectoryName(csproj);
                File.Delete(csproj);
                _logger.LogInformation("已删除排除项目文件：{Path}", csproj);
                if (
                    !string.IsNullOrEmpty(dir)
                    && Directory.Exists(dir)
                    && !Directory.EnumerateFileSystemEntries(dir).Any()
                )
                {
                    Directory.Delete(dir);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "删除排除项目文件失败：{Path}", csproj);
            }
        }
    }

    /// <summary>
    /// 从 .slnx（XML 格式）中移除被排除的 Project 节点
    /// </summary>
    private void RemoveProjectsFromSlnx(string slnxPath, HashSet<string> excludedFileNames)
    {
        var doc = System.Xml.Linq.XDocument.Load(
            slnxPath,
            System.Xml.Linq.LoadOptions.PreserveWhitespace
        );
        var removed = 0;
        // 兼容默认命名空间：用 LocalName 匹配
        var projectNodes = doc.Descendants()
            .Where(e =>
                string.Equals(e.Name.LocalName, "Project", StringComparison.OrdinalIgnoreCase)
            )
            .ToList();
        foreach (var node in projectNodes)
        {
            var pathAttr =
                node.Attribute("Path") ?? node.Attribute("path") ?? node.Attribute("PATH");
            if (pathAttr == null)
            {
                continue;
            }
            var fileName = Path.GetFileName(
                pathAttr.Value.Replace('\\', Path.DirectorySeparatorChar)
            );
            if (excludedFileNames.Contains(fileName))
            {
                node.Remove();
                removed++;
            }
        }
        if (removed > 0)
        {
            doc.Save(slnxPath);
            _logger.LogInformation("从 {Path} 中移除 {Count} 个项目引用", slnxPath, removed);
        }
    }

    /// <summary>
    /// 从 .sln（文本格式）中移除被排除的 Project / EndProject 块以及其在
    /// GlobalSection(ProjectConfigurationPlatforms) 与 GlobalSection(NestedProjects) 中的相关 GUID 行
    /// </summary>
    private void RemoveProjectsFromSln(string slnPath, HashSet<string> excludedFileNames)
    {
        var lines = File.ReadAllLines(slnPath);
        var output = new List<string>(lines.Length);
        var removedGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var removedCount = 0;

        // 第一阶段：扫描并移除 Project(...) ... EndProject 块，记录被删项目 GUID
        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];
            if (line.TrimStart().StartsWith("Project(", StringComparison.OrdinalIgnoreCase))
            {
                // Project("{TypeGuid}") = "Name", "Path\Project.csproj", "{ProjGuid}"
                var match = Regex.Match(
                    line,
                    @"^\s*Project\(""\{[^}]+\}""\)\s*=\s*""([^""]*)""\s*,\s*""([^""]*)""\s*,\s*""(\{[^}]+\})""",
                    RegexOptions.IgnoreCase
                );
                if (match.Success)
                {
                    var projectFile = Path.GetFileName(
                        match.Groups[2].Value.Replace('\\', Path.DirectorySeparatorChar)
                    );
                    if (excludedFileNames.Contains(projectFile))
                    {
                        // 跳过整个 Project ... EndProject 块
                        removedGuids.Add(match.Groups[3].Value);
                        removedCount++;
                        // 寻找匹配的 EndProject
                        while (
                            i < lines.Length
                            && !lines[i]
                                .TrimStart()
                                .StartsWith("EndProject", StringComparison.OrdinalIgnoreCase)
                        )
                        {
                            i++;
                        }
                        i++; // 跳过 EndProject 自身
                        continue;
                    }
                }
            }
            output.Add(line);
            i++;
        }

        // 第二阶段：移除 GlobalSection 中引用了被删 GUID 的行
        if (removedGuids.Count > 0)
        {
            output = output
                .Where(l =>
                    !removedGuids.Any(g => l.IndexOf(g, StringComparison.OrdinalIgnoreCase) >= 0)
                )
                .ToList();
        }

        if (removedCount > 0)
        {
            File.WriteAllLines(slnPath, output);
            _logger.LogInformation("从 {Path} 中移除 {Count} 个项目引用", slnPath, removedCount);
        }
    }

    /// <summary>
    /// 为框架内所有 csproj 注入 Debug/Release 输出路径属性组：
    /// OutputPath 统一指向 <c>{FrameworkPath}\Builds\{Configuration}</c>，
    /// Debug/Release 分别汇总到同一目录，便于统一查看与打包。
    /// 由于各 csproj 所处目录深度不同，回退路径 <c>..\</c> 数量按其相对 FrameworkPath 的层级动态生成。
    /// 已包含相同输出路径的 csproj 会被跳过，保证幂等。
    /// </summary>
    private void InjectOutputPathToProjects(
        string frameworkPath,
        CancellationToken cancellationToken
    )
    {
        // 输出路径片段标识，用于幂等判断（无论 Debug/Release 都包含此后缀）
        const string outputPathMarker = @"\Builds\";

        foreach (
            var csproj in Directory.EnumerateFiles(
                frameworkPath,
                "*.csproj",
                SearchOption.AllDirectories
            )
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var content = File.ReadAllText(csproj);
                // 已注入则跳过
                if (content.Contains(outputPathMarker, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // 计算 csproj 所在目录相对 FrameworkPath 的层级深度
                var csprojDir = Path.GetDirectoryName(csproj)!;
                var relative = Path.GetRelativePath(frameworkPath, csprojDir);
                var depth =
                    string.IsNullOrEmpty(relative) || relative == "."
                        ? 0
                        : relative
                            .Split(
                                new[]
                                {
                                    Path.DirectorySeparatorChar,
                                    Path.AltDirectorySeparatorChar,
                                },
                                StringSplitOptions.RemoveEmptyEntries
                            )
                            .Length;
                if (depth <= 0)
                {
                    // csproj 直接位于 FrameworkPath 根，按规范跳过（极少见）
                    continue;
                }

                // 构造回退前缀：每多一层目录，多一个 ..\
                var parentPrefix = string.Concat(Enumerable.Repeat(@"..\", depth));
                var debugOutput = $"{parentPrefix}Builds\\Debug";
                var releaseOutput = $"{parentPrefix}Builds\\Release";

                // 探测原文件换行风格，构造统一缩进的 PropertyGroup 片段
                var newLine = content.Contains("\r\n") ? "\r\n" : "\n";
                var injection =
                    newLine
                    + newLine
                    + "    <!-- Debug 配置：定义调试模式下的输出路径 -->"
                    + newLine
                    + "    <PropertyGroup Condition=\"'$(Configuration)'=='Debug'\">"
                    + newLine
                    + "        <!-- Debug Output Path 输出路径：调试构建的输出目录 -->"
                    + newLine
                    + $"        <OutputPath>{debugOutput}</OutputPath>"
                    + newLine
                    + "    </PropertyGroup>"
                    + newLine
                    + newLine
                    + "    <!-- Release 配置：定义发布模式下的输出路径 -->"
                    + newLine
                    + "    <PropertyGroup Condition=\"'$(Configuration)'=='Release'\">"
                    + newLine
                    + "        <!-- Release Output Path 输出路径：发布构建的输出目录 -->"
                    + newLine
                    + $"        <OutputPath>{releaseOutput}</OutputPath>"
                    + newLine
                    + "    </PropertyGroup>"
                    + newLine;

                // 在最后一个 </Project> 之前插入
                var endTagIndex = content.LastIndexOf(
                    "</Project>",
                    StringComparison.OrdinalIgnoreCase
                );
                if (endTagIndex < 0)
                {
                    _logger.LogWarning("未找到 </Project> 闭合标签，跳过：{Path}", csproj);
                    continue;
                }

                var modified = content.Insert(endTagIndex, injection);
                File.WriteAllText(csproj, modified, new UTF8Encoding(false));
                _logger.LogInformation("已注入输出路径（深度 {Depth}）：{Path}", depth, csproj);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "注入 csproj 输出路径失败：{Path}", csproj);
            }
        }
    }

    /// <summary>
    /// 扫描 FrameworkPath 下所有 .sln / .slnx，移除其中「磁盘上不存在的」Project 引用，
    /// 以及自身又引用了不存在 csproj 的「间接悬挂」项目（递归传播）。
    /// 部分上游解决方案保留了未发布的 ConsoleTestApp、Mysql、Cli.Core 等悬挂依赖，
    /// 会导致 dotnet build 抛出 MSB3202「未找到项目文件」或 CS0234/CS0246 等错误，
    /// 本步骤统一兜底清理。
    /// </summary>
    private void RemoveDanglingProjectsFromSolutions(
        string frameworkPath,
        CancellationToken cancellationToken
    )
    {
        // 先迭代计算所有「损坏的」csproj 全路径（直接缺失或链路上有缺失）
        var brokenProjectFileNames = CollectBrokenProjectFileNames(
            frameworkPath,
            cancellationToken
        );

        var solutionFiles = Directory
            .EnumerateFiles(frameworkPath, "*.sln", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(frameworkPath, "*.slnx", SearchOption.AllDirectories))
            .ToList();

        foreach (var solution in solutionFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var dangling = CollectDanglingProjectFileNames(solution, brokenProjectFileNames);
                if (dangling.Count == 0)
                {
                    continue;
                }
                _logger.LogWarning(
                    "解决方案 {Path} 检测到 {Count} 个悬挂/间接悬挂引用：{Files}",
                    solution,
                    dangling.Count,
                    string.Join(", ", dangling)
                );
                var ext = Path.GetExtension(solution);
                if (string.Equals(ext, ".slnx", StringComparison.OrdinalIgnoreCase))
                {
                    RemoveProjectsFromSlnx(solution, dangling);
                }
                else
                {
                    RemoveProjectsFromSln(solution, dangling);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "悬挂引用清理失败：{Path}", solution);
            }
        }
    }

    /// <summary>
    /// 迭代扫描所有 csproj/vbproj/fsproj，返回链路上含有「不存在引用」的项目文件名集合。
    /// 通过多轮迭代直到稳定，从而把传染性破坏一次性收敛干净。
    /// </summary>
    private HashSet<string> CollectBrokenProjectFileNames(
        string frameworkPath,
        CancellationToken cancellationToken
    )
    {
        var allProjects = Directory
            .EnumerateFiles(frameworkPath, "*.csproj", SearchOption.AllDirectories)
            .Concat(
                Directory.EnumerateFiles(frameworkPath, "*.vbproj", SearchOption.AllDirectories)
            )
            .Concat(
                Directory.EnumerateFiles(frameworkPath, "*.fsproj", SearchOption.AllDirectories)
            )
            .Select(Path.GetFullPath)
            .ToList();
        var allProjectSet = new HashSet<string>(allProjects, StringComparer.OrdinalIgnoreCase);
        var brokenFullPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool changed;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            changed = false;
            foreach (var csproj in allProjects)
            {
                if (brokenFullPaths.Contains(csproj))
                {
                    continue;
                }
                System.Xml.Linq.XDocument doc;
                try
                {
                    doc = System.Xml.Linq.XDocument.Load(csproj);
                }
                catch
                {
                    continue;
                }
                var csprojDir = Path.GetDirectoryName(csproj)!;
                foreach (
                    var refNode in doc.Descendants()
                        .Where(e =>
                            string.Equals(
                                e.Name.LocalName,
                                "ProjectReference",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                )
                {
                    var include =
                        refNode.Attribute("Include")?.Value ?? refNode.Attribute("include")?.Value;
                    if (string.IsNullOrWhiteSpace(include))
                    {
                        continue;
                    }
                    string referenced;
                    try
                    {
                        referenced = Path.GetFullPath(
                            Path.Combine(
                                csprojDir,
                                include.Replace('\\', Path.DirectorySeparatorChar)
                            )
                        );
                    }
                    catch
                    {
                        continue;
                    }
                    var missingOnDisk =
                        !allProjectSet.Contains(referenced) && !File.Exists(referenced);
                    if (missingOnDisk || brokenFullPaths.Contains(referenced))
                    {
                        if (brokenFullPaths.Add(csproj))
                        {
                            changed = true;
                            _logger.LogWarning(
                                "项目 {Project} 因引用 {Reference} 而被标记为间接悬挂",
                                csproj,
                                referenced
                            );
                        }
                        break;
                    }
                }
            }
        } while (changed);

        return new HashSet<string>(
            brokenFullPaths.Select(Path.GetFileName)!,
            StringComparer.OrdinalIgnoreCase
        );
    }

    /// <summary>
    /// 解析单个解决方案文件，返回其中引用的项目文件在磁盘上不存在或属于「间接悬挂」的文件名集合（仅文件名）。
    /// 同时支持 .sln（文本格式）与 .slnx（XML 格式）。
    /// </summary>
    private static HashSet<string> CollectDanglingProjectFileNames(
        string solutionPath,
        HashSet<string> additionalBrokenFileNames
    )
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var solutionDir = Path.GetDirectoryName(solutionPath)!;
        var ext = Path.GetExtension(solutionPath);

        if (string.Equals(ext, ".slnx", StringComparison.OrdinalIgnoreCase))
        {
            System.Xml.Linq.XDocument doc;
            try
            {
                doc = System.Xml.Linq.XDocument.Load(solutionPath);
            }
            catch
            {
                return result;
            }
            foreach (
                var node in doc.Descendants()
                    .Where(e =>
                        string.Equals(
                            e.Name.LocalName,
                            "Project",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
            )
            {
                var pathAttr =
                    node.Attribute("Path") ?? node.Attribute("path") ?? node.Attribute("PATH");
                if (pathAttr == null || string.IsNullOrWhiteSpace(pathAttr.Value))
                {
                    continue;
                }
                var rel = pathAttr.Value.Replace('\\', Path.DirectorySeparatorChar);
                var full = Path.GetFullPath(Path.Combine(solutionDir, rel));
                var fileName = Path.GetFileName(rel);
                if (!File.Exists(full) || additionalBrokenFileNames.Contains(fileName))
                {
                    result.Add(fileName);
                }
            }
            return result;
        }

        // .sln 文本解析：Project("{Type}") = "Name", "Path\xx.csproj", "{Guid}"
        var projectLineRegex = new Regex(
            @"^\s*Project\(""\{[^}]+\}""\)\s*=\s*""([^""]*)""\s*,\s*""([^""]*)""\s*,\s*""\{[^}]+\}""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );
        foreach (var line in File.ReadAllLines(solutionPath))
        {
            var match = projectLineRegex.Match(line);
            if (!match.Success)
            {
                continue;
            }
            var rel = match.Groups[2].Value.Replace('\\', Path.DirectorySeparatorChar);
            // 仅检查 csproj 等真实项目；解决方案文件夹的 Path 为名称，不带扩展名，跳过
            var subExt = Path.GetExtension(rel);
            if (string.IsNullOrEmpty(subExt))
            {
                continue;
            }
            var full = Path.GetFullPath(Path.Combine(solutionDir, rel));
            var fileName = Path.GetFileName(rel);
            if (!File.Exists(full) || additionalBrokenFileNames.Contains(fileName))
            {
                result.Add(fileName);
            }
        }
        return result;
    }

    /// <summary>
    /// 把 <c>aspnet-core</c>（即所有 Extract_* 路径的最近公共父目录）下的 *.targets 文件
    /// 同步到每个 sln/slnx 所在目录。已存在同名文件则跳过，缺失则复制；
    /// 这样无论 dotnet build 从哪个 sln 目录执行，都能就近拿到完整的 targets 配置。
    /// </summary>
    private void SyncTargetsToSolutionDirectories(
        RepositoryConfig repository,
        CancellationToken cancellationToken
    )
    {
        var sourceTargetsDir = ResolveAspNetCoreRoot(repository);
        if (string.IsNullOrEmpty(sourceTargetsDir) || !Directory.Exists(sourceTargetsDir))
        {
            _logger.LogWarning("未能定位 targets 源目录，跳过 targets 同步");
            return;
        }
        var sourceTargets = Directory
            .EnumerateFiles(sourceTargetsDir, "*.targets", SearchOption.TopDirectoryOnly)
            .ToList();
        if (sourceTargets.Count == 0)
        {
            _logger.LogInformation("源目录 {Dir} 不含 *.targets，无需同步", sourceTargetsDir);
            return;
        }

        var solutionFiles = Directory
            .EnumerateFiles(repository.FrameworkPath, "*.sln", SearchOption.AllDirectories)
            .Concat(
                Directory.EnumerateFiles(
                    repository.FrameworkPath,
                    "*.slnx",
                    SearchOption.AllDirectories
                )
            )
            .ToList();

        foreach (var solution in solutionFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var solutionDir = Path.GetDirectoryName(solution);
            if (string.IsNullOrEmpty(solutionDir))
            {
                continue;
            }
            // 解决方案就在 aspnet-core 自身目录时无需复制
            if (
                string.Equals(
                    Path.GetFullPath(solutionDir).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(sourceTargetsDir).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                continue;
            }
            foreach (var src in sourceTargets)
            {
                var fileName = Path.GetFileName(src);
                var dest = Path.Combine(solutionDir, fileName);
                if (File.Exists(dest))
                {
                    continue;
                }
                try
                {
                    File.Copy(src, dest, overwrite: false);
                    _logger.LogInformation("已复制 targets：{Src} → {Dest}", src, dest);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "复制 targets 失败：{Src} → {Dest}", src, dest);
                }
            }
        }
    }

    /// <summary>
    /// 取所有 Extract_* 配置路径（FrameworkPath/GatewaysPath/ServicePath/ModulePath）的最近公共父目录，
    /// 通常即 <c>{FrameworkPath}/aspnet-core</c>。
    /// </summary>
    private static string ResolveAspNetCoreRoot(RepositoryConfig repository)
    {
        var candidates = new[]
        {
            repository.Extract_FrameworkPath,
            repository.Extract_GatewaysPath,
            repository.Extract_ServicePath,
            repository.Extract_ModulePath,
        }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Replace('\\', '/').Trim('/'))
            .ToList();
        if (candidates.Count == 0)
        {
            return repository.FrameworkPath;
        }
        // 第一段（如 "aspnet-core"）若全部一致即作为公共根
        var firstSegments = candidates
            .Select(p => p.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (firstSegments.Count == 1 && !string.IsNullOrEmpty(firstSegments[0]))
        {
            return Path.Combine(repository.FrameworkPath, firstSegments[0]!);
        }
        return repository.FrameworkPath;
    }

    /// <summary>
    /// 为 FrameworkPath 下每个 csproj 的同级目录补齐 <c>icon.png</c>。
    /// 若同级已存在则保留；否则用统一应用图标 (Assets/AuroraApplication.png) 复制并写入。
    /// </summary>
    private void EnsureIconForEveryProject(
        RepositoryConfig repository,
        CancellationToken cancellationToken
    )
    {
        var unifiedImageSource = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "AuroraApplication.png"
        );
        if (!File.Exists(unifiedImageSource))
        {
            _logger.LogWarning("未找到统一图片资源：{Path}，跳过 icon 同步", unifiedImageSource);
            return;
        }
        var iconName = Path.GetFileName(repository.ReplaceIconPath?.Trim() ?? string.Empty);
        if (string.IsNullOrEmpty(iconName))
        {
            iconName = "icon.png";
        }
        var imageBytes = LoadAndCompressImage(unifiedImageSource, 900 * 1024);

        var projectFiles = Directory
            .EnumerateFiles(repository.FrameworkPath, "*.csproj", SearchOption.AllDirectories)
            .Concat(
                Directory.EnumerateFiles(
                    repository.FrameworkPath,
                    "*.vbproj",
                    SearchOption.AllDirectories
                )
            )
            .Concat(
                Directory.EnumerateFiles(
                    repository.FrameworkPath,
                    "*.fsproj",
                    SearchOption.AllDirectories
                )
            );

        var addedCount = 0;
        foreach (var csproj in projectFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = Path.GetDirectoryName(csproj);
            if (string.IsNullOrEmpty(dir))
            {
                continue;
            }
            var dest = Path.Combine(dir, iconName);
            if (File.Exists(dest))
            {
                continue;
            }
            try
            {
                File.WriteAllBytes(dest, imageBytes);
                addedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "写入图标失败：{Path}", dest);
            }
        }
        _logger.LogInformation("已为 {Count} 个项目目录补齐 {Icon}", addedCount, iconName);
    }

    /// 2) 同时用 Assets/AuroraApplication.png 替换项目中其它常见图片文件（按扩展名匹配）。
    /// </summary>
    private void ReplaceIconsAndImages(
        RepositoryConfig repository,
        CancellationToken cancellationToken
    )
    {
        // 解析「统一图标」源文件路径：固定使用应用 Assets 下的 AuroraApplication.png
        var unifiedImageSource = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "AuroraApplication.png"
        );
        if (!File.Exists(unifiedImageSource))
        {
            _logger.LogWarning("未找到统一图片资源：{Path}，跳过图标/图片替换", unifiedImageSource);
            return;
        }

        // 预生成「压缩后图片字节」（NuGet 要求 icon ≤ 1MB，统一阈值 900KB 留余量）
        var unifiedImageBytes = LoadAndCompressImage(unifiedImageSource, 900 * 1024);

        // 1) 同名图标覆盖：例如 icon.png → 找到所有 icon.png 用统一图片覆盖
        var iconName = repository.ReplaceIconPath?.Trim();
        if (!string.IsNullOrEmpty(iconName))
        {
            // 仅取文件名部分，避免误把路径当成绝对源
            var pureIconName = Path.GetFileName(iconName);
            var iconReplaceCount = 0;
            foreach (
                var file in Directory.EnumerateFiles(
                    repository.FrameworkPath,
                    pureIconName,
                    SearchOption.AllDirectories
                )
            )
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    WriteImageBytes(file, unifiedImageBytes);
                    iconReplaceCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "替换图标失败：{Path}", file);
                }
            }
            _logger.LogInformation(
                "已用统一图片覆盖 {Count} 个 {Name}",
                iconReplaceCount,
                pureIconName
            );
        }

        // 2) 用统一图片覆盖项目内常见图片资源（按扩展名匹配）
        var imageReplaceCount = 0;
        foreach (
            var file in Directory.EnumerateFiles(
                repository.FrameworkPath,
                "*.*",
                SearchOption.AllDirectories
            )
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ext = Path.GetExtension(file);
            if (string.IsNullOrEmpty(ext) || !ImageFileExtensions.Contains(ext))
            {
                continue;
            }
            // 跳过源文件自身（即便不太可能位于目标目录内）
            if (string.Equals(file, unifiedImageSource, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            try
            {
                WriteImageBytes(file, unifiedImageBytes);
                imageReplaceCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "替换图片失败：{Path}", file);
            }
        }
        _logger.LogInformation("已用统一图片覆盖 {Count} 个图片资源", imageReplaceCount);
    }

    /// <summary>
    /// 加载源图片，必要时按比例缩小并以 PNG 重新编码，直到字节数不超过 maxBytes。
    /// 用于满足 NuGet 图标 ≤ 1MB 的限制。
    /// </summary>
    private byte[] LoadAndCompressImage(string sourcePath, int maxBytes)
    {
        var original = File.ReadAllBytes(sourcePath);
        if (original.Length <= maxBytes)
        {
            return original;
        }

        try
        {
            // 解码源图（缓存到内存，避免占用文件句柄）
            BitmapFrame frame;
            using (var ms = new MemoryStream(original))
            {
                var decoder = BitmapDecoder.Create(
                    ms,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad
                );
                frame = decoder.Frames[0];
            }

            var width = frame.PixelWidth;
            var height = frame.PixelHeight;

            // 多轮缩放：每轮按 0.85 倍缩小，直至达到目标体积或像素过小
            for (var i = 0; i < 12; i++)
            {
                var scale = Math.Pow(0.85, i + 1);
                var targetWidth = Math.Max(64, (int)(width * scale));
                var targetHeight = Math.Max(64, (int)(height * scale));

                var scaled = new TransformedBitmap(
                    frame,
                    new System.Windows.Media.ScaleTransform(
                        targetWidth / (double)width,
                        targetHeight / (double)height
                    )
                );

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(scaled));
                using var output = new MemoryStream();
                encoder.Save(output);
                var bytes = output.ToArray();
                if (bytes.Length <= maxBytes)
                {
                    _logger.LogInformation(
                        "统一图标已压缩：{OriginalKB}KB → {NewKB}KB（{W}x{H}）",
                        original.Length / 1024,
                        bytes.Length / 1024,
                        targetWidth,
                        targetHeight
                    );
                    return bytes;
                }
                if (targetWidth <= 64 || targetHeight <= 64)
                {
                    _logger.LogWarning(
                        "图标已缩至 {W}x{H}（{KB}KB），仍超过阈值 {MaxKB}KB",
                        targetWidth,
                        targetHeight,
                        bytes.Length / 1024,
                        maxBytes / 1024
                    );
                    return bytes;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "图标压缩失败，回退使用原始字节：{Path}", sourcePath);
        }

        return original;
    }

    /// <summary>
    /// 将给定字节写入目标文件（覆盖写入，先清除只读属性）
    /// </summary>
    private static void WriteImageBytes(string targetPath, byte[] bytes)
    {
        if (File.Exists(targetPath))
        {
            var attrs = File.GetAttributes(targetPath);
            if ((attrs & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
            {
                File.SetAttributes(targetPath, attrs & ~FileAttributes.ReadOnly);
            }
        }
        File.WriteAllBytes(targetPath, bytes);
    }

    /// <summary>
    /// 文件内容替换：仅文本类文件，按 Key 升序逐条执行正则替换；
    /// 全字匹配（\b 边界）+ 大小写敏感
    /// </summary>
    private void ReplaceFileContents(
        string root,
        IReadOnlyList<ReplaceRule> rules,
        CancellationToken cancellationToken
    )
    {
        if (rules.Count == 0)
        {
            return;
        }

        // 预编译正则：性能 + 显式区分大小写
        var compiledRules = rules
            .Select(r => new
            {
                Rule = r,
                Regex = new Regex(
                    $@"\b{Regex.Escape(r.OldValue)}\b",
                    RegexOptions.Compiled | RegexOptions.CultureInvariant
                ),
            })
            .ToList();

        foreach (var filePath in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsTextFile(filePath))
            {
                continue;
            }
            try
            {
                // 读取时尽量保留原编码：默认按 UTF-8（带 BOM 检测）
                var encoding = DetectEncoding(filePath, out var hasBom);
                var original = File.ReadAllText(filePath, encoding);
                var modified = original;
                foreach (var item in compiledRules)
                {
                    modified = item.Regex.Replace(modified, item.Rule.NewValue);
                }
                if (!string.Equals(original, modified, StringComparison.Ordinal))
                {
                    // 写回时保持 BOM 设置
                    var writeEncoding = encoding switch
                    {
                        UTF8Encoding => new UTF8Encoding(hasBom),
                        _ => encoding,
                    };
                    File.WriteAllText(filePath, modified, writeEncoding);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "替换文件内容失败：{Path}", filePath);
            }
        }
    }

    /// <summary>
    /// 是否为允许处理的文本类文件
    /// </summary>
    private static bool IsTextFile(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (!string.IsNullOrEmpty(ext) && TextFileExtensions.Contains(ext))
        {
            return true;
        }
        var name = Path.GetFileName(filePath);
        return TextFileNames.Contains(name);
    }

    /// <summary>
    /// 简易编码探测：识别 UTF-8 BOM / UTF-16 BE/LE BOM；其余按 UTF-8（无 BOM）处理
    /// </summary>
    private static Encoding DetectEncoding(string filePath, out bool hasBom)
    {
        hasBom = false;
        Span<byte> buffer = stackalloc byte[4];
        using var fs = File.OpenRead(filePath);
        var read = fs.Read(buffer);
        if (read >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
        {
            hasBom = true;
            return new UTF8Encoding(true);
        }
        if (read >= 2 && buffer[0] == 0xFF && buffer[1] == 0xFE)
        {
            hasBom = true;
            return Encoding.Unicode;
        }
        if (read >= 2 && buffer[0] == 0xFE && buffer[1] == 0xFF)
        {
            hasBom = true;
            return Encoding.BigEndianUnicode;
        }
        return new UTF8Encoding(false);
    }

    #endregion
}
