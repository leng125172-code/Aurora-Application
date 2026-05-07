namespace Lion.AbpPro.Oidc;

public interface IExternalLoginProvider
{
    /// <summary>
    /// 获取access_token
    /// </summary>
    Task<GetAccessTokenResult> GetAccessTokenAsync(string code);

    /// <summary>
    /// 获取用户信息
    /// </summary>
    Task<GetUserInfoResult> GetUserInfoAsync(string accessToken);
}
