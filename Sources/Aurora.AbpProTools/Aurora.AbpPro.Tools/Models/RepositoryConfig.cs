namespace Aurora.AbpPro.Tools.Models;

public class RepositoryConfig
{
    /// <summary>
    /// 默认构造函数（用于对象初始化器场景）
    /// </summary>
    public RepositoryConfig() { }

    /// <summary>
    /// 通过 Owner / RepositoryId 快速构建（匿名访问）
    /// </summary>
    /// <param name="owner">仓库拥有者</param>
    /// <param name="repositoryId">仓库 Id（仓库名）</param>
    public RepositoryConfig(string owner, string repositoryId)
    {
        Owner = owner;
        RepositoryId = repositoryId;
    }

    /// <summary>
    /// 通过 Owner / RepositoryId / Token 快速构建（认证访问）
    /// </summary>
    public RepositoryConfig(string owner, string repositoryId, string token)
        : this(owner, repositoryId)
    {
        Token = token;
    }

    public RepositoryType RepositoryType { get; set; } = RepositoryType.Source;

    /// <summary>
    /// 仓库拥有者
    /// </summary>
    public string Owner { get; set; } = string.Empty;

    /// <summary>
    /// 仓库Id
    /// </summary>
    public string RepositoryId { get; set; } = string.Empty;

    /// <summary>
    /// 描述
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Github Token
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// 版本
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// 提取目录
    /// </summary>
    public string ExtractDirectory { get; set; } = string.Empty;

    /// <summary>
    /// 更新后的框架名称
    /// </summary>
    public string FrameworkName { get; set; } = string.Empty;

    /// <summary>
    /// 更新后的框架版本
    /// </summary>
    public string FrameworkVersion => Version;

    /// <summary>
    /// 框架的路径
    /// </summary>
    public string FrameworkPath { get; set; } = string.Empty;

    /// <summary>
    /// 解压后的框架源代码路径
    /// </summary>
    public string Extract_FrameworkPath { get; set; } = string.Empty;

    /// <summary>
    /// 解压后的网关源代码路径
    /// </summary>
    public string Extract_GatewaysPath { get; set; } = string.Empty;

    /// <summary>
    /// 解压后的服务源代码路径
    /// </summary>
    public string Extract_ServicePath { get; set; } = string.Empty;

    /// <summary>
    /// 解压后的模块源代码路径
    /// </summary>
    public string Extract_ModulePath { get; set; } = string.Empty;

    /// <summary>
    /// 解压后的模板源代码路径
    /// </summary>
    public string Extract_Templates { get; set; } = string.Empty;

    public Dictionary<int, string> ReplaceContent { get; set; } = new Dictionary<int, string>();

    /// <summary>
    /// 复制源码时需要排除的文件夹（按目录名匹配，区分大小写：建议传入 .github、obj、bin 等）
    /// </summary>
    public List<string> ExcludeFolders { get; set; } = new List<string>();

    /// <summary>
    /// 复制源码时需要排除的文件（按文件名匹配，支持简单通配符：* 与 ?，例如 *.md）
    /// </summary>
    public List<string> ExcludeFiles { get; set; } = new List<string>();

    /// <summary>
    /// 替换图标的目标文件名（例如 icon.png）。
    /// 复制完成后将用应用 Assets 目录下的统一图标覆盖项目中所有同名文件，
    /// 同时也会用统一图标替换项目中其他常见图片资源（按扩展名）。
    /// </summary>
    public string ReplaceIconPath { get; set; } = string.Empty;

    public List<string> ExcludeProj { get; set; } = new List<string>();

    /// <summary>
    /// 版本来源：决定执行「下载框架」时如何确定该仓库要下载的版本号
    /// </summary>
    public VersionSource VersionSource { get; set; } = VersionSource.LatestRelease;
}

public enum RepositoryType
{
    Business,
    Source,
}

/// <summary>
/// 仓库下载版本来源
/// </summary>
public enum VersionSource
{
    /// <summary>
    /// 取仓库自身最新 Release
    /// </summary>
    LatestRelease,

    /// <summary>
    /// 从 abp-vnext-pro/abp 解压目录下 aspnet-core 中所有 *.targets 解析得到
    /// </summary>
    AbpVNextProTargets,

    /// <summary>
    /// 从 abpframework/abp 解压目录根目录的 *.props 解析得到
    /// </summary>
    AbpFrameworkProps,
}
