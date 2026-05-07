namespace Lion.AbpPro.Oidc.DingTalk;

public class GetDingTalkUserInfoResponse
{
    /// <summary>
    /// openId
    /// </summary>
    public string openId { get; set; }

    /// <summary>
    /// 用户昵称
    /// </summary>
    public string nick { get; set; }

    /// <summary>
    /// 用户头像
    /// </summary>
    public string avatarUrl { get; set; }

    /// <summary>
    /// 用户邮箱
    /// </summary>
    public string email { get; set; }

    /// <summary>
    /// 用户手机号
    /// </summary>
    public string mobile { get; set; }
}
