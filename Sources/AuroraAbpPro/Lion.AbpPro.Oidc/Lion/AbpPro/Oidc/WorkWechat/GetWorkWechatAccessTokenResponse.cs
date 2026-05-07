namespace Lion.AbpPro.Oidc.WorkWechat;

public class GetWorkWechatAccessTokenResponse
{
    /// <summary>
    /// 错误码
    /// </summary>
    public int errcode { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string errmsg { get; set; }

    /// <summary>
    /// access_token
    /// </summary>
    public string access_token { get; set; }

    /// <summary>
    /// 过期时间(秒)
    /// </summary>
    public int expires_in { get; set; }
}
