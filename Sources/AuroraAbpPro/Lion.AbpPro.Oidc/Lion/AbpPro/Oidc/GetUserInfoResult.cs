namespace Lion.AbpPro.Oidc;

public class GetUserInfoResult
{
    public GetUserInfoResult(
        string id,
        string avatarUrl,
        string userName,
        string name,
        string email
    )
    {
        Id = id;
        AvatarUrl = avatarUrl;
        Name = name;
        UserName = userName;
        Email = email;
        ExtraProperties = new Dictionary<string, string>();
    }

    public string Id { get; set; }

    /// <summary>
    /// 头像地址
    /// </summary>
    public string AvatarUrl { get; set; }

    /// <summary>
    /// 用户名
    /// </summary>
    public string UserName { get; set; }

    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 昵称
    /// </summary>
    public string NickName { get; set; }

    /// <summary>
    /// 邮箱
    /// </summary>
    public string Email { get; set; }

    /// <summary>
    /// 手机号
    /// </summary>
    public string Mobile { get; set; }

    /// <summary>
    /// 扩展
    /// </summary>
    public Dictionary<string, string> ExtraProperties { get; set; }
}
