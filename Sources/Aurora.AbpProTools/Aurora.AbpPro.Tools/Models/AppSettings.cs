namespace Aurora.AbpPro.Tools.Models;

/// <summary>
/// 应用配置模型：持久化用户偏好（语言、皮肤等）
/// </summary>
public class AppSettings
{
    /// <summary>
    /// 应用标题
    /// </summary>
    public string Title { get; set; } = "Aurora AbpPro Tools";

    /// <summary>
    /// 当前语言代码（zh-CN / en-US）
    /// </summary>
    public string Language { get; set; } = "zh-CN";

    /// <summary>
    /// 皮肤名称（Default / Dark / Violet）
    /// </summary>
    public string Skin { get; set; } = "Default";

    /// <summary>
    /// GitHub 个人访问令牌（PAT）；用于调用 GitHub API 时认证
    /// </summary>
    public string GitHubToken { get; set; } = string.Empty;
}
