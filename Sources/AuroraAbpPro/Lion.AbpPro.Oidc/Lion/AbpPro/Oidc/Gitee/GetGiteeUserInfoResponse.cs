namespace Lion.AbpPro.Oidc.Gitee;

public class GetGiteeUserInfoResponse
{
    public long id { get; set; }

    /// <summary>
    /// 头像地址
    /// </summary>
    public string avatar_url { get; set; }

    /// <summary>
    /// 名称
    /// </summary>
    public string name { get; set; }

    /// <summary>
    /// 邮箱
    /// </summary>
    public string email { get; set; }
}
