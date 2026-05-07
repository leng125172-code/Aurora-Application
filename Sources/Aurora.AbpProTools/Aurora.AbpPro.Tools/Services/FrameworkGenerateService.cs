using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Aurora.AbpPro.Tools.Models;
using Microsoft.Extensions.Logging;

namespace Aurora.AbpPro.Tools.Services;

/// <summary>
/// 框架生成服务实现：
/// 1) 按读取结果得到需要拷贝的项目列表
/// 2) 直接把完整项目文件夹拷贝到 Sources\AuroraAbpPro 根目录
/// 3) 不保留源目录层级、不覆盖已存在内容、不重命名、不重写项目文件
/// </summary>
public class FrameworkGenerateService : IFrameworkGenerateService
{
    private readonly ILogger<FrameworkGenerateService> _logger;

    public FrameworkGenerateService(ILogger<FrameworkGenerateService> logger)
    {
        _logger = logger;
    }

    public Task<FrameworkGenerateResult> GenerateAsync(
        FrameworkReadResult readResult,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() => GenerateCore(readResult, cancellationToken), cancellationToken);
    }

    private FrameworkGenerateResult GenerateCore(
        FrameworkReadResult readResult,
        CancellationToken ct
    )
    {
        var result = new FrameworkGenerateResult
        {
            SlnxPath = AuroraGenerateConfig.SolutionSlnxPath,
        };
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 1) 准备目标根目录
            EnsureSourceRoot(result);

            // 2) 确保 Sources/AuroraAbpPro 下存在 Directory.Build.targets，
            //    防止 ABP 框架项目 WarningsAsErrors=Nullable 设置被提升为编译错误。
            EnsureAuroraAbpProDirectoryBuildTargets(result);

            // 3) 为每个项目计算目标项目目录，目标目录直接使用项目文件夹名称。
            var plan = BuildPlan(readResult, result, ct);
            if (plan.Count == 0)
            {
                result.Error = "未发现可生成的项目（FrameworkReadResult.AllProjects 为空？）";
                return result;
            }
            WriteProcessLog($"去重后总数：{plan.Count}");

            // 4) 安全拷贝完整项目目录：已存在则跳过，不覆盖。
            int copiedProjects = 0;
            int skippedProjects = 0;
            int copiedFiles = 0;
            foreach (var item in plan)
            {
                ct.ThrowIfCancellationRequested();
                var copyResult = CopyProjectDirectory(item, result);
                if (copyResult.ProjectCopied)
                {
                    copiedProjects++;
                    copiedFiles += copyResult.FileCount;
                }
                else
                {
                    skippedProjects++;
                }
            }

            // 5) 补齐复制项目里缺失的 Import 目标文件。
            var importedFileCount = EnsureMissingImportsForCopiedProjects(plan, result, ct);

            // 6) 修复项目文件中错误嵌套在 ItemGroup 等非根节点内的 Import 元素（避免 MSB4232）。
            var fixedMisplacedImportsCount = FixMisplacedImportsInProjects(result, ct);

            // 7) 读取 Sources 下全部项目，将可匹配的 PackageReference 转换为 ProjectReference。
            var projectIndex = BuildSourcesProjectIndex(result);
            var packageVersionIndex = BuildPackageVersionIndex(result);
            var rewrittenProjects = RewritePackageReferencesToProjectReferences(
                projectIndex,
                packageVersionIndex,
                result,
                ct
            );

            // 8) 修正 Sources 下所有项目中 common.props 的 Import 路径（相对路径）。
            var fixedCommonPropsCount = FixCommonPropsImportInAllProjects(projectIndex, result, ct);

            // 9) 按项目文件路径整理并格式化根解决方案的 Folder/Project 结构。
            var addedToSolutionCount = UpdateRootSolutionSlnx(plan, result, ct);

            // 10) 对复制项目下所有文本文件做格式化整理。
            var formattedFileCount = FormatCopiedProjectFiles(plan, result, ct);

            stopwatch.Stop();
            var wpfCount = plan.Count(p => p.IsWpfProject);
            var hostCount = plan.Count(p => p.IsHostProject);
            result.CopiedProjectCount = copiedProjects;
            result.CopiedFileCount = copiedFiles;
            result.RewrittenCsprojCount = rewrittenProjects;
            result.Messages.Add(
                $"总共匹配项目数：{plan.Count}（其中 WPF 项目数：{wpfCount}、Host 项目数：{hostCount}）"
            );
            result.Messages.Add($"实际拷贝数：{copiedProjects}");
            result.Messages.Add($"跳过数：{skippedProjects}");
            result.Messages.Add($"补齐 Import 文件数：{importedFileCount}");
            result.Messages.Add($"修复嵌套 Import 数：{fixedMisplacedImportsCount}");
            result.Messages.Add($"更新项目引用数：{rewrittenProjects}");
            result.Messages.Add($"修正 common.props Import 数：{fixedCommonPropsCount}");
            result.Messages.Add($"写入根解决方案项目数：{addedToSolutionCount}");
            result.Messages.Add($"格式化文件数：{formattedFileCount}");
            result.Messages.Add($"耗时：{stopwatch.Elapsed}");
            WriteProcessLog(
                $"总共匹配项目数：{plan.Count}（其中 WPF 项目数：{wpfCount}、Host 项目数：{hostCount}）"
            );
            WriteProcessLog($"实际拷贝数：{copiedProjects}");
            WriteProcessLog($"跳过数：{skippedProjects}");
            WriteProcessLog($"补齐 Import 文件数：{importedFileCount}");
            WriteProcessLog($"修复嵌套 Import 数：{fixedMisplacedImportsCount}");
            WriteProcessLog($"更新项目引用数：{rewrittenProjects}");
            WriteProcessLog($"修正 common.props Import 数：{fixedCommonPropsCount}");
            WriteProcessLog($"写入根解决方案项目数：{addedToSolutionCount}");
            WriteProcessLog($"格式化文件数：{formattedFileCount}");
            WriteProcessLog($"耗时：{stopwatch.Elapsed}");

            result.Success = true;
            return result;
        }
        catch (OperationCanceledException)
        {
            result.Error = "已取消";
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成框架失败");
            result.Error = ex.ToString();
            return result;
        }
    }

    // ============== Step 1: 准备目标根 ==============

    /// <summary>
    /// 确保框架目标目录存在，不删除任何已有内容。
    /// </summary>
    private static void EnsureSourceRoot(FrameworkGenerateResult result)
    {
        Directory.CreateDirectory(AuroraGenerateConfig.SolutionRoot);
        Directory.CreateDirectory(AuroraGenerateConfig.SourceRoot);
        result.Messages.Add($"已确认框架目标目录：{AuroraGenerateConfig.SourceRoot}");
    }

    // ============== Step 2: 构建复制计划 ==============

    /// <summary>单个项目的复制计划</summary>
    private class PlanItem
    {
        public string OldProjectName { get; init; } = string.Empty;
        public string OldCsprojPath { get; init; } = string.Empty;
        public string OldProjectDir { get; init; } = string.Empty;
        public string NewProjectDir { get; init; } = string.Empty;
        public bool IsWpfProject { get; init; }
        public bool IsHostProject { get; init; }
    }

    private List<PlanItem> BuildPlan(
        FrameworkReadResult readResult,
        FrameworkGenerateResult result,
        CancellationToken ct
    )
    {
        var list = new List<PlanItem>();
        // 项目去重：若包映射与项目引用同时指向同一 csproj，只生成一份
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in readResult.AllProjects.Values)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(p.FilePath) || !File.Exists(p.FilePath))
            {
                continue;
            }
            var oldDir = Path.GetDirectoryName(p.FilePath)!;
            if (!seen.Add(oldDir))
            {
                continue;
            }
            var newDir = Path.Combine(AuroraGenerateConfig.SourceRoot, Path.GetFileName(oldDir));
            list.Add(
                new PlanItem
                {
                    OldProjectName = p.Name,
                    OldCsprojPath = p.FilePath,
                    OldProjectDir = oldDir,
                    NewProjectDir = newDir,
                    IsWpfProject = p.IsWpfProject,
                    IsHostProject = p.IsHostProject,
                }
            );
        }

        // 同一目标项目目录下若出现冲突，后入者忽略。
        var dedup = new Dictionary<string, PlanItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in list)
        {
            var key = item.NewProjectDir;
            if (!dedup.ContainsKey(key))
            {
                dedup[key] = item;
            }
            else
            {
                result.Messages.Add(
                    $"目标目录冲突已忽略：{item.OldProjectName} → {item.NewProjectDir}"
                );
            }
        }
        return dedup.Values.ToList();
    }

    // ============== Step 3: 复制项目目录 ==============

    /// <summary>
    /// 复制项目目录全部内容到目标位置，目标已存在时直接跳过，不覆盖任何文件。
    /// </summary>
    private CopyProjectResult CopyProjectDirectory(PlanItem item, FrameworkGenerateResult result)
    {
        var kindText = FormatProjectKinds(item);
        WriteProcessLog($"正在拷贝：{item.OldProjectDir} → {item.NewProjectDir}{kindText}");

        if (Directory.Exists(item.NewProjectDir))
        {
            var skipMessage = $"已跳过：{item.NewProjectDir}";
            result.Messages.Add(skipMessage);
            WriteProcessLog(skipMessage);
            return new CopyProjectResult(false, 0);
        }

        int count = 0;

        try
        {
            Directory.CreateDirectory(item.NewProjectDir);
            foreach (
                var file in Directory.EnumerateFiles(
                    item.OldProjectDir,
                    "*",
                    SearchOption.AllDirectories
                )
            )
            {
                var rel = Path.GetRelativePath(item.OldProjectDir, file);
                if (ShouldSkipPath(rel))
                {
                    continue;
                }
                var dest = Path.Combine(item.NewProjectDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                if (File.Exists(dest))
                {
                    continue;
                }
                File.Copy(file, dest, overwrite: false);
                count++;
            }
            result.Messages.Add(
                $"拷贝完成：{item.OldProjectDir} → {item.NewProjectDir}，文件 {count} 个{kindText}"
            );
            WriteProcessLog("拷贝完成");
            return new CopyProjectResult(true, count);
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or PathTooLongException)
        {
            var message =
                $"拷贝失败：{item.OldProjectDir} → {item.NewProjectDir}，原因：{ex.Message}";
            result.Messages.Add(message);
            WriteProcessLog(message);
            _logger.LogWarning(
                ex,
                "拷贝项目失败：{Source} → {Target}",
                item.OldProjectDir,
                item.NewProjectDir
            );
            return new CopyProjectResult(false, count);
        }
    }

    /// <summary>
    /// 项目拷贝结果。
    /// </summary>
    private readonly record struct CopyProjectResult(bool ProjectCopied, int FileCount);

    /// <summary>
    /// 建立 Sources 目录下项目名到 csproj 实际路径的索引。
    /// </summary>
    private Dictionary<string, string> BuildSourcesProjectIndex(FrameworkGenerateResult result)
    {
        var projectIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sourcesRoot = Path.Combine(AuroraGenerateConfig.SolutionRoot, "Sources");
        if (!Directory.Exists(sourcesRoot))
        {
            return projectIndex;
        }

        WriteProcessLog($"正在扫描：{sourcesRoot}");
        foreach (
            var csprojPath in Directory.EnumerateFiles(
                sourcesRoot,
                "*.csproj",
                SearchOption.AllDirectories
            )
        )
        {
            var projectName = Path.GetFileNameWithoutExtension(csprojPath);
            if (projectIndex.ContainsKey(projectName))
            {
                result.Messages.Add($"项目索引冲突已忽略：{projectName} → {csprojPath}");
                continue;
            }
            projectIndex[projectName] = csprojPath;
        }

        result.Messages.Add($"Sources 目录项目索引完成：{projectIndex.Count} 个项目");
        return projectIndex;
    }

    /// <summary>
    /// 将目标目录项目中的 PackageReference 转换为指向实际项目相对路径的 ProjectReference。
    /// </summary>
    private int RewritePackageReferencesToProjectReferences(
        IReadOnlyDictionary<string, string> projectIndex,
        IReadOnlyDictionary<string, string> packageVersionIndex,
        FrameworkGenerateResult result,
        CancellationToken ct
    )
    {
        int rewrittenProjects = 0;
        foreach (
            var csprojPath in projectIndex.Values.OrderBy(
                path => path,
                StringComparer.OrdinalIgnoreCase
            )
        )
        {
            ct.ThrowIfCancellationRequested();
            if (
                RewritePackageReferencesToProjectReferences(
                    csprojPath,
                    projectIndex,
                    packageVersionIndex,
                    result
                )
            )
            {
                rewrittenProjects++;
            }
        }
        return rewrittenProjects;
    }

    /// <summary>
    /// 将单个项目文件中可匹配的 PackageReference 转换为 ProjectReference。
    /// </summary>
    private bool RewritePackageReferencesToProjectReferences(
        string csprojPath,
        IReadOnlyDictionary<string, string> projectIndex,
        IReadOnlyDictionary<string, string> packageVersionIndex,
        FrameworkGenerateResult result
    )
    {
        XDocument document;
        try
        {
            document = XDocument.Load(csprojPath, LoadOptions.PreserveWhitespace);
        }
        catch (Exception ex)
        {
            var message = $"解析项目文件失败：{csprojPath}，原因：{ex.Message}";
            result.Messages.Add(message);
            _logger.LogWarning(ex, "解析项目文件失败：{Path}", csprojPath);
            return false;
        }

        var projectDirectory = Path.GetDirectoryName(csprojPath)!;
        var changed = false;
        foreach (
            var packageReference in document
                .Descendants()
                .Where(e => e.Name.LocalName == "PackageReference")
                .ToList()
        )
        {
            var packageName = packageReference.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(packageName))
            {
                continue;
            }
            if (!projectIndex.TryGetValue(packageName, out var targetProjectPath))
            {
                if (
                    EnsurePackageReferenceVersion(
                        packageReference,
                        packageName,
                        packageVersionIndex,
                        csprojPath,
                        result
                    )
                )
                {
                    changed = true;
                }
                continue;
            }
            if (string.Equals(csprojPath, targetProjectPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(projectDirectory, targetProjectPath)
                .Replace('/', '\\');
            var projectReference = new XElement(
                packageReference.Name.Namespace + "ProjectReference",
                new XAttribute("Include", relativePath)
            );
            packageReference.ReplaceWith(projectReference);
            changed = true;
            WriteProcessLog(
                $"更新引用：{csprojPath} PackageReference={packageName} → ProjectReference={relativePath}"
            );
        }

        if (!changed)
        {
            return false;
        }

        document.Save(csprojPath, SaveOptions.None);
        result.Messages.Add($"已更新项目引用：{csprojPath}");
        return true;
    }

    /// <summary>
    /// 将本次生成涉及的项目同步写入根解决方案 slnx。
    /// 已存在的项目路径会自动跳过，保证重复执行幂等。
    /// </summary>
    private int UpdateRootSolutionSlnx(
        IReadOnlyCollection<PlanItem> plan,
        FrameworkGenerateResult result,
        CancellationToken ct
    )
    {
        var slnxPath = AuroraGenerateConfig.SolutionSlnxPath;
        if (!File.Exists(slnxPath))
        {
            result.Messages.Add($"未找到根解决方案文件，已跳过写入：{slnxPath}");
            return 0;
        }

        XDocument document;
        try
        {
            document = XDocument.Load(slnxPath, LoadOptions.PreserveWhitespace);
        }
        catch (Exception ex)
        {
            result.Messages.Add($"加载根解决方案失败：{slnxPath}，原因：{ex.Message}");
            _logger.LogWarning(ex, "加载根解决方案失败：{Path}", slnxPath);
            return 0;
        }

        var root = document.Root;
        if (root == null)
        {
            result.Messages.Add($"根解决方案格式无效，已跳过写入：{slnxPath}");
            return 0;
        }

        var ns = root.Name.Namespace;

        // 先建立现有项目路径索引，避免重复添加。
        var existingProjectPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var projectNode in root.Descendants().Where(e => e.Name.LocalName == "Project"))
        {
            var pathAttr =
                projectNode.Attribute("Path")
                ?? projectNode.Attribute("path")
                ?? projectNode.Attribute("PATH");
            if (pathAttr == null || string.IsNullOrWhiteSpace(pathAttr.Value))
            {
                continue;
            }
            existingProjectPaths.Add(NormalizeSlnxPath(pathAttr.Value));
        }

        // 建立现有 Folder 索引，后续按路径层级补齐。
        var folderIndex = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var folderNode in root.Elements().Where(e => e.Name.LocalName == "Folder"))
        {
            var name = folderNode.Attribute("Name")?.Value;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }
            folderIndex[NormalizeFolderName(name)] = folderNode;
        }

        var addedCount = 0;
        foreach (var item in plan.OrderBy(p => p.NewProjectDir, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();

            var targetCsprojPath = Path.Combine(
                item.NewProjectDir,
                Path.GetFileName(item.OldCsprojPath)
            );
            if (!File.Exists(targetCsprojPath))
            {
                var fallbackCsprojPath = Directory
                    .EnumerateFiles(item.NewProjectDir, "*.csproj", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault();
                if (string.IsNullOrWhiteSpace(fallbackCsprojPath))
                {
                    continue;
                }
                targetCsprojPath = fallbackCsprojPath;
            }

            var relativePath = Path.GetRelativePath(
                AuroraGenerateConfig.SolutionRoot,
                targetCsprojPath
            );
            var normalizedPath = NormalizeSlnxPath(relativePath);
            if (!existingProjectPaths.Add(normalizedPath))
            {
                continue;
            }

            // 按项目路径构建 Folder 层级，例如 /Sources/AuroraAbpPro/ProjectFolder/
            var folderPath =
                Path.GetDirectoryName(normalizedPath)?.Replace('\\', '/') ?? string.Empty;
            var targetFolderName = NormalizeFolderName($"/{folderPath}/");
            var targetFolder = EnsureFolderNode(root, ns, folderIndex, targetFolderName);

            var projectNode = new XElement(
                ns + "Project",
                new XAttribute("Path", normalizedPath),
                CreateDefaultPlatformNodes(ns)
            );
            targetFolder.Add(projectNode);
            addedCount++;
        }

        // 统一按 Name/Path 排序，保证结构稳定且可读。
        SortSlnxNodes(root);

        if (addedCount <= 0)
        {
            // 即使没有新增，也保存一次以应用排序/格式化。
            document.Save(slnxPath, SaveOptions.None);
            result.Messages.Add($"已整理并格式化根解决方案：{slnxPath}");
            return 0;
        }

        try
        {
            document.Save(slnxPath, SaveOptions.None);
            result.Messages.Add($"已同步到根解决方案：{slnxPath}，新增 {addedCount} 个项目");
            return addedCount;
        }
        catch (Exception ex)
        {
            result.Messages.Add($"保存根解决方案失败：{slnxPath}，原因：{ex.Message}");
            _logger.LogWarning(ex, "保存根解决方案失败：{Path}", slnxPath);
            return 0;
        }
    }

    /// <summary>
    /// 生成默认平台映射，保持与当前 slnx 中 C# 项目配置一致。
    /// </summary>
    private static IEnumerable<XElement> CreateDefaultPlatformNodes(XNamespace ns)
    {
        yield return new XElement(
            ns + "Platform",
            new XAttribute("Solution", "*|Any CPU"),
            new XAttribute("Project", "x64")
        );
        yield return new XElement(
            ns + "Platform",
            new XAttribute("Solution", "*|arm64"),
            new XAttribute("Project", "arm64")
        );
        yield return new XElement(
            ns + "Platform",
            new XAttribute("Solution", "*|x64"),
            new XAttribute("Project", "x64")
        );
        yield return new XElement(
            ns + "Platform",
            new XAttribute("Solution", "*|x86"),
            new XAttribute("Project", "x86")
        );
    }

    /// <summary>
    /// 将路径统一为 slnx 使用的正斜杠格式。
    /// </summary>
    private static string NormalizeSlnxPath(string path)
    {
        return path.Replace('\\', '/');
    }

    /// <summary>
    /// 规范化 Folder 节点名为 /a/b/c/ 形式。
    /// </summary>
    private static string NormalizeFolderName(string folderName)
    {
        var normalized = folderName.Replace('\\', '/').Trim('/');
        return string.IsNullOrWhiteSpace(normalized) ? "/" : $"/{normalized}/";
    }

    /// <summary>
    /// 按层级补齐 Folder 节点，确保 slnx 结构与项目路径一致。
    /// </summary>
    private static XElement EnsureFolderNode(
        XElement root,
        XNamespace ns,
        IDictionary<string, XElement> folderIndex,
        string normalizedFolderName
    )
    {
        if (folderIndex.TryGetValue(normalizedFolderName, out var existing))
        {
            return existing;
        }

        var segments = normalizedFolderName
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = "/";
        XElement? lastNode = null;
        foreach (var segment in segments)
        {
            current = current == "/" ? $"/{segment}/" : $"{current}{segment}/";
            if (folderIndex.TryGetValue(current, out var node))
            {
                lastNode = node;
                continue;
            }

            var newNode = new XElement(ns + "Folder", new XAttribute("Name", current));
            root.Add(newNode);
            folderIndex[current] = newNode;
            lastNode = newNode;
        }

        return lastNode ?? throw new InvalidOperationException("无法创建 Folder 节点");
    }

    /// <summary>
    /// 对 slnx 的 Folder/Project 节点做稳定排序。
    /// </summary>
    private static void SortSlnxNodes(XElement root)
    {
        var children = root.Elements().ToList();
        var configs = children.Where(e => e.Name.LocalName == "Configurations").ToList();
        var folders = children
            .Where(e => e.Name.LocalName == "Folder")
            .OrderBy(
                e => NormalizeFolderName(e.Attribute("Name")?.Value ?? string.Empty),
                StringComparer.OrdinalIgnoreCase
            )
            .ToList();
        var others = children
            .Where(e => e.Name.LocalName != "Configurations" && e.Name.LocalName != "Folder")
            .ToList();

        foreach (var folder in folders)
        {
            var projects = folder
                .Elements()
                .Where(e => e.Name.LocalName == "Project")
                .OrderBy(
                    e => NormalizeSlnxPath(e.Attribute("Path")?.Value ?? string.Empty),
                    StringComparer.OrdinalIgnoreCase
                )
                .ToList();
            folder.RemoveNodes();
            foreach (var project in projects)
            {
                folder.Add(project);
            }
        }

        root.RemoveNodes();
        foreach (var node in configs)
        {
            root.Add(node);
        }
        foreach (var node in folders)
        {
            root.Add(node);
        }
        foreach (var node in others)
        {
            root.Add(node);
        }
    }

    /// <summary>
    /// 补齐复制项目里缺失的 Import 目标文件：优先使用原始项目中对应 Import 文件，
    /// 将其复制到新项目期望的位置（若位置不可写则回退到解决方案根目录）。
    /// 同时自动修正 csproj 里的 Import 路径为 $(SolutionDir)xxx.props（如果目标文件被补到根目录）。
    /// </summary>
    private int EnsureMissingImportsForCopiedProjects(
        IReadOnlyCollection<PlanItem> plan,
        FrameworkGenerateResult result,
        CancellationToken ct
    )
    {
        var copiedCount = 0;
        var solutionRootFull = Path.GetFullPath(AuroraGenerateConfig.SolutionRoot);

        foreach (var item in plan)
        {
            ct.ThrowIfCancellationRequested();
            var copiedCsprojPath = ResolveCopiedCsprojPath(item);
            if (string.IsNullOrWhiteSpace(copiedCsprojPath) || !File.Exists(copiedCsprojPath))
            {
                continue;
            }

            XDocument copiedDoc;
            try
            {
                copiedDoc = XDocument.Load(copiedCsprojPath, LoadOptions.PreserveWhitespace);
            }
            catch
            {
                continue;
            }

            var copiedDir = Path.GetDirectoryName(copiedCsprojPath)!;
            var oldDir = Path.GetDirectoryName(item.OldCsprojPath)!;

            bool docChanged = false;

            foreach (
                var importNode in copiedDoc.Descendants().Where(e => e.Name.LocalName == "Import")
            )
            {
                var importProject = importNode.Attribute("Project")?.Value;
                if (
                    string.IsNullOrWhiteSpace(importProject)
                    || importProject.Contains("$(", StringComparison.Ordinal)
                )
                {
                    continue;
                }

                var normalizedRel = importProject
                    .Replace('/', Path.DirectorySeparatorChar)
                    .Replace('\\', Path.DirectorySeparatorChar);
                var expectedPath = Path.GetFullPath(Path.Combine(copiedDir, normalizedRel));
                if (File.Exists(expectedPath))
                {
                    continue;
                }

                var sourcePath = Path.GetFullPath(Path.Combine(oldDir, normalizedRel));
                if (!File.Exists(sourcePath))
                {
                    sourcePath = TryFindImportSourceInFramework(sourcePath);
                }
                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                {
                    result.Messages.Add(
                        $"未找到 Import 源文件：{importProject}（项目：{copiedCsprojPath}）"
                    );
                    continue;
                }

                // 目标路径：优先原始相对路径，否则回退根目录
                var destinationPath = IsPathUnderRoot(expectedPath, solutionRootFull)
                    ? expectedPath
                    : Path.Combine(AuroraGenerateConfig.SolutionRoot, Path.GetFileName(sourcePath));

                // 如果目标路径在根目录，自动修正 Import 路径为 $(SolutionDir)xxx.props
                if (!IsPathUnderRoot(destinationPath, copiedDir))
                {
                    var newImport = "$(SolutionDir)" + Path.GetFileName(destinationPath);
                    if (importNode.Attribute("Project")?.Value != newImport)
                    {
                        importNode.SetAttributeValue("Project", newImport);
                        docChanged = true;
                        result.Messages.Add(
                            $"已修正 Import 路径：{copiedCsprojPath} → {newImport}"
                        );
                    }
                }

                if (!File.Exists(destinationPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                    File.Copy(sourcePath, destinationPath, overwrite: false);
                    copiedCount++;
                    result.Messages.Add($"已补齐 Import：{destinationPath}");
                }
            }

            if (docChanged)
            {
                copiedDoc.Save(copiedCsprojPath, SaveOptions.None);
            }
        }

        return copiedCount;
    }

    /// <summary>
    /// 在 FrameworkSourceRoot 中按文件名查找 Import 源文件。
    /// </summary>
    private static string? TryFindImportSourceInFramework(string originalExpectedPath)
    {
        var fileName = Path.GetFileName(originalExpectedPath);
        if (
            string.IsNullOrWhiteSpace(fileName)
            || !Directory.Exists(AuroraGenerateConfig.FrameworkSourceRoot)
        )
        {
            return null;
        }

        return Directory
            .EnumerateFiles(
                AuroraGenerateConfig.FrameworkSourceRoot,
                fileName,
                SearchOption.AllDirectories
            )
            .FirstOrDefault();
    }

    /// <summary>
    /// 解析复制后项目文件路径。
    /// </summary>
    private static string? ResolveCopiedCsprojPath(PlanItem item)
    {
        var expected = Path.Combine(item.NewProjectDir, Path.GetFileName(item.OldCsprojPath));
        if (File.Exists(expected))
        {
            return expected;
        }
        return Directory
            .EnumerateFiles(item.NewProjectDir, "*.csproj", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();
    }

    /// <summary>
    /// 判断路径是否位于指定根目录下。
    /// </summary>
    private static bool IsPathUnderRoot(string fullPath, string rootFullPath)
    {
        var normalizedPath =
            Path.GetFullPath(fullPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedRoot =
            Path.GetFullPath(rootFullPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 对复制项目目录中的全部文本文件进行格式化整理：
    /// 1) 统一 CRLF 换行；2) 移除行尾多余空白；3) XML 文件进行结构化缩进保存。
    /// </summary>
    private int FormatCopiedProjectFiles(
        IReadOnlyCollection<PlanItem> plan,
        FrameworkGenerateResult result,
        CancellationToken ct
    )
    {
        var formattedCount = 0;
        var visitedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in plan)
        {
            ct.ThrowIfCancellationRequested();
            if (!visitedDirs.Add(item.NewProjectDir) || !Directory.Exists(item.NewProjectDir))
            {
                continue;
            }

            foreach (
                var filePath in Directory.EnumerateFiles(
                    item.NewProjectDir,
                    "*",
                    SearchOption.AllDirectories
                )
            )
            {
                ct.ThrowIfCancellationRequested();
                if (!IsFormattingTextFile(filePath))
                {
                    continue;
                }

                if (TryFormatFile(filePath))
                {
                    formattedCount++;
                }
            }
        }

        if (formattedCount > 0)
        {
            result.Messages.Add($"已格式化复制项目文件：{formattedCount} 个");
        }
        return formattedCount;
    }

    /// <summary>
    /// 单文件格式化。
    /// </summary>
    private static bool TryFormatFile(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (
            string.Equals(ext, ".csproj", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".props", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".targets", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".xml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".xaml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".config", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".slnx", StringComparison.OrdinalIgnoreCase)
        )
        {
            if (TryFormatXml(filePath))
            {
                return true;
            }
        }

        var encoding = DetectEncoding(filePath, out var hasBom);
        var original = File.ReadAllText(filePath, encoding);
        var normalized = NormalizeText(original);
        if (string.Equals(original, normalized, StringComparison.Ordinal))
        {
            return false;
        }

        var writeEncoding = encoding is UTF8Encoding ? new UTF8Encoding(hasBom) : encoding;
        File.WriteAllText(filePath, normalized, writeEncoding);
        return true;
    }

    /// <summary>
    /// 尝试格式化 XML 文件。
    /// </summary>
    private static bool TryFormatXml(string filePath)
    {
        try
        {
            var before = File.ReadAllText(filePath, Encoding.UTF8);
            var document = XDocument.Load(filePath, LoadOptions.None);
            document.Save(filePath, SaveOptions.None);
            var after = File.ReadAllText(filePath, Encoding.UTF8);
            return !string.Equals(before, after, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 文本规范化：统一 CRLF，并移除每行结尾空白。
    /// </summary>
    private static string NormalizeText(string input)
    {
        var unified = input.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = unified.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].TrimEnd(' ', '\t');
        }
        return string.Join("\r\n", lines);
    }

    /// <summary>
    /// 判定是否参与格式化的文本文件。
    /// </summary>
    private static bool IsFormattingTextFile(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (string.IsNullOrWhiteSpace(ext))
        {
            var name = Path.GetFileName(filePath);
            return string.Equals(name, "Dockerfile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "README", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "LICENSE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "NuGet.Config", StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(ext, ".cs", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".csproj", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".props", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".targets", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".xml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".xaml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".config", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".md", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".slnx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".yml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".yaml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".js", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".ts", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".css", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".scss", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".less", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 编码探测：识别 UTF-8/UTF-16 BOM，其余按 UTF-8 无 BOM。
    /// </summary>
    private static Encoding DetectEncoding(string filePath, out bool hasBom)
    {
        hasBom = false;
        Span<byte> header = stackalloc byte[4];
        using var fs = File.OpenRead(filePath);
        var read = fs.Read(header);
        if (read >= 3 && header[0] == 0xEF && header[1] == 0xBB && header[2] == 0xBF)
        {
            hasBom = true;
            return new UTF8Encoding(true);
        }
        if (read >= 2 && header[0] == 0xFF && header[1] == 0xFE)
        {
            hasBom = true;
            return Encoding.Unicode;
        }
        if (read >= 2 && header[0] == 0xFE && header[1] == 0xFF)
        {
            hasBom = true;
            return Encoding.BigEndianUnicode;
        }
        return new UTF8Encoding(false);
    }

    /// <summary>
    /// 从全局依赖项目目录下的 props 和 targets 文件建立包版本索引。
    /// </summary>
    private Dictionary<string, string> BuildPackageVersionIndex(FrameworkGenerateResult result)
    {
        var packageVersionIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(AuroraGenerateConfig.FrameworkSourceRoot))
        {
            return packageVersionIndex;
        }

        foreach (
            var filePath in EnumeratePropsAndTargetsFiles(AuroraGenerateConfig.FrameworkSourceRoot)
        )
        {
            XDocument document;
            try
            {
                document = XDocument.Load(filePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "解析版本配置文件失败：{Path}", filePath);
                continue;
            }

            foreach (var element in document.Descendants())
            {
                var localName = element.Name.LocalName;
                if (
                    !string.Equals(
                        localName,
                        "PackageReference",
                        StringComparison.OrdinalIgnoreCase
                    )
                    && !string.Equals(
                        localName,
                        "PackageVersion",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    continue;
                }

                var packageName =
                    element.Attribute("Include")?.Value
                    ?? element.Attribute("Update")?.Value
                    ?? element.Attribute("Name")?.Value;
                if (string.IsNullOrWhiteSpace(packageName))
                {
                    continue;
                }

                var version =
                    element.Attribute("Version")?.Value
                    ?? element.Element(element.Name.Namespace + "Version")?.Value;
                if (
                    string.IsNullOrWhiteSpace(version)
                    || packageVersionIndex.ContainsKey(packageName)
                )
                {
                    continue;
                }

                packageVersionIndex[packageName] = version;
            }
        }

        result.Messages.Add($"全局包版本索引完成：{packageVersionIndex.Count} 个包版本");
        return packageVersionIndex;
    }

    /// <summary>
    /// 枚举全局依赖项目目录下的 props 和 targets 文件。
    /// </summary>
    private static IEnumerable<string> EnumeratePropsAndTargetsFiles(string root)
    {
        foreach (
            var propsFile in Directory.EnumerateFiles(root, "*.props", SearchOption.AllDirectories)
        )
        {
            yield return propsFile;
        }
        foreach (
            var targetsFile in Directory.EnumerateFiles(
                root,
                "*.targets",
                SearchOption.AllDirectories
            )
        )
        {
            yield return targetsFile;
        }
    }

    /// <summary>
    /// 为无法转换为 ProjectReference 且缺失 Version 的 PackageReference 回填版本。
    /// </summary>
    private bool EnsurePackageReferenceVersion(
        XElement packageReference,
        string packageName,
        IReadOnlyDictionary<string, string> packageVersionIndex,
        string csprojPath,
        FrameworkGenerateResult result
    )
    {
        if (HasPackageReferenceVersion(packageReference))
        {
            return false;
        }
        if (!packageVersionIndex.TryGetValue(packageName, out var version))
        {
            result.Messages.Add($"未找到包版本：{packageName}（项目：{csprojPath}）");
            return false;
        }

        packageReference.SetAttributeValue("Version", version);
        result.Messages.Add(
            $"已回填包版本：{csprojPath} PackageReference={packageName} Version={version}"
        );
        WriteProcessLog($"回填版本：{csprojPath} PackageReference={packageName} Version={version}");
        return true;
    }

    /// <summary>
    /// 判断 PackageReference 是否已经存在版本信息。
    /// </summary>
    private static bool HasPackageReferenceVersion(XElement packageReference)
    {
        var version =
            packageReference.Attribute("Version")?.Value
            ?? packageReference.Element(packageReference.Name.Namespace + "Version")?.Value;
        return !string.IsNullOrWhiteSpace(version);
    }

    /// <summary>
    /// 格式化项目类型标识。
    /// </summary>
    private static string FormatProjectKinds(PlanItem item)
    {
        var kinds = new List<string>();
        if (item.IsWpfProject)
        {
            kinds.Add("WPF");
        }
        if (item.IsHostProject)
        {
            kinds.Add("Host");
        }
        return kinds.Count > 0 ? $"（{string.Join("/", kinds)} 项目）" : string.Empty;
    }

    /// <summary>
    /// 写入流程日志。
    /// </summary>
    private static void WriteProcessLog(string message)
    {
        Console.WriteLine(message);
    }

    /// <summary>
    /// 修正 Sources 目录下所有项目中 common.props 的 Import 方式：
    /// 1) 若存在 <c>$(SolutionDir)common.props</c> 形式的 Import，替换为相对路径；
    /// 2) 若不存在任何 common.props 的 Import，则在首个 Import 或 PropertyGroup 之前插入相对路径 Import。
    /// </summary>
    private int FixCommonPropsImportInAllProjects(
        IReadOnlyDictionary<string, string> projectIndex,
        FrameworkGenerateResult result,
        CancellationToken ct
    )
    {
        const string CommonPropsFileName = "common.props";
        var commonPropsFullPath = Path.GetFullPath(
            Path.Combine(AuroraGenerateConfig.SolutionRoot, CommonPropsFileName)
        );

        if (!File.Exists(commonPropsFullPath))
        {
            result.Messages.Add($"未找到 common.props 文件，已跳过修正：{commonPropsFullPath}");
            return 0;
        }

        int fixedCount = 0;
        foreach (
            var csprojPath in projectIndex.Values.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
        )
        {
            ct.ThrowIfCancellationRequested();

            XDocument document;
            try
            {
                document = XDocument.Load(csprojPath, LoadOptions.PreserveWhitespace);
            }
            catch (Exception ex)
            {
                result.Messages.Add(
                    $"解析项目文件失败（common.props 修正）：{csprojPath}，原因：{ex.Message}"
                );
                continue;
            }

            var csprojDir = Path.GetDirectoryName(csprojPath)!;
            // 计算 common.props 相对于当前 csproj 目录的相对路径，统一使用反斜杠
            var relativePath = Path.GetRelativePath(csprojDir, commonPropsFullPath)
                .Replace('/', '\\');

            // 查找所有 Import 节点中名称以 common.props 结尾的节点
            var commonPropsImport = document
                .Descendants()
                .Where(e => e.Name.LocalName == "Import")
                .FirstOrDefault(e =>
                    (e.Attribute("Project")?.Value ?? string.Empty).EndsWith(
                        CommonPropsFileName,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

            bool changed;
            if (commonPropsImport != null)
            {
                var existingValue = commonPropsImport.Attribute("Project")!.Value;
                if (string.Equals(existingValue, relativePath, StringComparison.OrdinalIgnoreCase))
                {
                    // 已经是正确的相对路径，无需修改
                    continue;
                }

                // 替换为相对路径（覆盖 $(SolutionDir)common.props 或其他写法）
                commonPropsImport.SetAttributeValue("Project", relativePath);
                changed = true;
                WriteProcessLog($"已修正 common.props 路径：{csprojPath} → {relativePath}");
                result.Messages.Add($"已修正 common.props Import：{csprojPath}");
            }
            else
            {
                // 不存在 common.props Import，在合适位置插入
                var root = document.Root;
                if (root == null)
                {
                    continue;
                }

                var ns = root.Name.Namespace;
                var newImport = new XElement(
                    ns + "Import",
                    new XAttribute("Project", relativePath)
                );

                // 插入策略：放在第一个 Import 之前；若无则放在第一个 PropertyGroup 之前；否则作为根下首个子元素
                var firstImport = root.Elements().FirstOrDefault(e => e.Name.LocalName == "Import");
                if (firstImport != null)
                {
                    firstImport.AddBeforeSelf(newImport);
                }
                else
                {
                    var firstPropertyGroup = root.Elements()
                        .FirstOrDefault(e => e.Name.LocalName == "PropertyGroup");
                    if (firstPropertyGroup != null)
                    {
                        firstPropertyGroup.AddBeforeSelf(newImport);
                    }
                    else
                    {
                        root.AddFirst(newImport);
                    }
                }

                changed = true;
                WriteProcessLog($"已添加 common.props Import：{csprojPath} → {relativePath}");
                result.Messages.Add($"已添加 common.props Import：{csprojPath}");
            }

            if (changed)
            {
                document.Save(csprojPath, SaveOptions.None);
                fixedCount++;
            }
        }

        return fixedCount;
    }

    // ============== Step 2: 确保 Directory.Build.targets ==============

    /// <summary>
    /// 在 Sources/AuroraAbpPro 目录下创建 Directory.Build.targets，
    /// 覆盖 ABP 框架项目中 &lt;WarningsAsErrors&gt;Nullable&lt;/WarningsAsErrors&gt; 的设置，
    /// 避免 nullable 警告被提升为编译错误。若文件已存在则跳过。
    /// </summary>
    private static void EnsureAuroraAbpProDirectoryBuildTargets(FrameworkGenerateResult result)
    {
        var targetsPath = Path.Combine(AuroraGenerateConfig.SourceRoot, "Directory.Build.targets");
        if (File.Exists(targetsPath))
        {
            result.Messages.Add($"Directory.Build.targets 已存在，已跳过创建：{targetsPath}");
            return;
        }

        var doc = new XDocument(
            new XComment(
                "\n  Directory.Build.targets\n"
                    + "  功能：在所有 AuroraAbpPro 子项目中覆盖 ABP 框架原始的 WarningsAsErrors 设置\n"
                    + "  说明：ABP 框架项目使用 <WarningsAsErrors>Nullable</WarningsAsErrors> 强制可空检查，\n"
                    + "        但本项目通过 common.props 统一管理警告策略，故此处清除该设置。\n"
            ),
            new XElement(
                "Project",
                new XElement(
                    "PropertyGroup",
                    new XComment(
                        " 清除 ABP 框架项目中的 WarningsAsErrors=Nullable 设置，避免 nullable 警告被提升为错误 "
                    ),
                    new XElement("TreatWarningsAsErrors", "false"),
                    new XElement("WarningsAsErrors", string.Empty)
                )
            )
        );
        doc.Save(targetsPath, SaveOptions.None);
        result.Messages.Add($"已创建 Directory.Build.targets：{targetsPath}");
        WriteProcessLog($"已创建 Directory.Build.targets：{targetsPath}");
    }

    // ============== Step 6: 修复错误嵌套的 Import 元素 ==============

    /// <summary>
    /// 扫描 Sources 目录下所有 csproj 文件，找出错误嵌套在 ItemGroup 等非 Project
    /// 根节点内的 Import 元素（执行 PackageReference 转换等操作后可能产生此问题），
    /// 并将其移除。直接位于 Project 根节点下的 Import 元素保留不变。
    /// </summary>
    private int FixMisplacedImportsInProjects(FrameworkGenerateResult result, CancellationToken ct)
    {
        var sourcesRoot = Path.Combine(AuroraGenerateConfig.SolutionRoot, "Sources");
        if (!Directory.Exists(sourcesRoot))
        {
            return 0;
        }

        int fixedFiles = 0;
        foreach (
            var csprojPath in Directory.EnumerateFiles(
                sourcesRoot,
                "*.csproj",
                SearchOption.AllDirectories
            )
        )
        {
            ct.ThrowIfCancellationRequested();

            XDocument document;
            try
            {
                document = XDocument.Load(csprojPath, LoadOptions.PreserveWhitespace);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "解析项目文件失败（Import 嵌套修复）：{Path}", csprojPath);
                continue;
            }

            var changed = false;

            // 找到所有 Import 节点，若其父节点不是 Project 根节点则删除（修复 MSB4232）
            foreach (
                var importNode in document
                    .Descendants()
                    .Where(e => e.Name.LocalName == "Import")
                    .ToList()
            )
            {
                var parent = importNode.Parent;
                if (parent == null || ReferenceEquals(parent, document.Root))
                {
                    // Import 直接位于 Project 根节点下，位置正确，保留
                    continue;
                }

                importNode.Remove();
                changed = true;
            }

            if (!changed)
            {
                continue;
            }

            try
            {
                document.Save(csprojPath, SaveOptions.None);
                fixedFiles++;
                result.Messages.Add($"已修复错误嵌套 Import：{csprojPath}");
                WriteProcessLog($"已修复错误嵌套 Import：{csprojPath}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "保存项目文件失败（Import 嵌套修复）：{Path}", csprojPath);
            }
        }

        return fixedFiles;
    }

    /// <summary>跳过 obj/bin 等无需复制的中间目录</summary>
    private static bool ShouldSkipPath(string relativePath)
    {
        var segments = relativePath.Split(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar
        );
        foreach (var seg in segments)
        {
            if (
                string.Equals(seg, "obj", StringComparison.OrdinalIgnoreCase)
                || string.Equals(seg, "bin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(seg, ".vs", StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }
        }
        return false;
    }
}
