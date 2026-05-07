namespace Lion.AbpPro.Oidc;

public class GetAccessTokenResult
{
    public GetAccessTokenResult(string accessToken)
    {
        AccessToken = accessToken;
        ExtraProperties = new Dictionary<string, string>();
    }

    /// <summary>
    /// access_token
    /// </summary>
    public string AccessToken { get; set; }

    /// <summary>
    /// 扩展
    /// </summary>
    public Dictionary<string, string> ExtraProperties { get; set; }
}
