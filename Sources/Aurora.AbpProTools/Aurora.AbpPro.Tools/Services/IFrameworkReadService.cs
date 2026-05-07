using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aurora.AbpPro.Tools.Models;
using Aurora.AbpPro.Tools.ViewModels;

namespace Aurora.AbpPro.Tools.Services;

/// <summary>
/// 框架读取服务：读取业务应用项目中的 PackageReference，
/// 在本地框架源码目录中按“包名 = csproj 文件名”规则递归展开，串成完整项目关联图。
/// </summary>
public interface IFrameworkReadService
{
    /// <summary>
    /// 执行依赖收集流水线。要求业务应用目录和本地框架源码目录都存在。
    /// </summary>
    Task<FrameworkReadResult> ReadAsync(
        IReadOnlyList<RepositoryReleaseItem> items,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// 框架读取结果
/// </summary>
public class FrameworkReadResult
{
    /// <summary>
    /// BFS 起点项目（来自业务应用 PackageReference 首次匹配到的框架项目）
    /// </summary>
    public List<CsProjectInfo> SeedProjects { get; } = new();

    /// <summary>
    /// 已串到的全部项目（包含模板自身），按 Name 去重
    /// </summary>
    public Dictionary<string, CsProjectInfo> AllProjects { get; } =
        new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 包引用 → 真实 csproj 路径映射（包名 = csproj 文件名约定一致才能映射上）
    /// </summary>
    public Dictionary<string, string> PackagePathMap { get; } =
        new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 已发现但未在 4 库中找到对应 csproj 的包（如 Microsoft.* / Newtonsoft.Json 之类的三方包）
    /// </summary>
    public Dictionary<string, string> UnresolvedPackages { get; } =
        new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 项目 → 关联到的下游项目（边集合，只记录已落到 4 库内的边，便于树形渲染）
    /// </summary>
    public Dictionary<string, List<string>> Edges { get; } =
        new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 可视化依赖树文本
    /// </summary>
    public string TreeText { get; set; } = string.Empty;
}

/// <summary>
/// csproj 项目信息
/// </summary>
public class CsProjectInfo
{
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// csproj 绝对路径
    /// </summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>
    /// 来源标识（如 local-framework）
    /// </summary>
    public string SourceRepo { get; init; } = string.Empty;

    /// <summary>
    /// 节点深度（模板 = 0，依次 +1）
    /// </summary>
    public int Depth { get; set; }

    /// <summary>
    /// 是否为 WPF 项目
    /// </summary>
    public bool IsWpfProject { get; set; }

    /// <summary>
    /// 是否为 Host 项目
    /// </summary>
    public bool IsHostProject { get; set; }
}

/// <summary>
/// PackageReference 信息（仅日志/调试用）
/// </summary>
public class PackageRefInfo
{
    public string Name { get; init; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string ProjectPath { get; set; } = string.Empty;
}
