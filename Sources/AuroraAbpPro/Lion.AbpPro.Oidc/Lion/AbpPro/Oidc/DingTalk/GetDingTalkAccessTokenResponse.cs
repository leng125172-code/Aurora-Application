namespace Lion.AbpPro.Oidc.DingTalk;

public class GetDingTalkAccessTokenResponse
{
    /// <summary>
    /// access_token
    /// </summary>
    public string accessToken { get; set; }

    /// <summary>
    /// 过期时间(秒)
    /// </summary>
    public int expireIn { get; set; }
}
