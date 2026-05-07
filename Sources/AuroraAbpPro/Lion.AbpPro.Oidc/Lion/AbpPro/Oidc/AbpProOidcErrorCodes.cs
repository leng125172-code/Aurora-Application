namespace Lion.AbpPro.Oidc;

public class AbpProOidcErrorCodes
{
    private const string NameSpace = "Lion.AbpPro.Oidc";

    /// <summary>
    /// 第三方登录host地址为空
    /// </summary>
    public const string ErrorCode100001 = NameSpace + ":100001";

    /// <summary>
    /// 第三方登录host地址格式错误
    /// </summary>
    public const string ErrorCode100002 = NameSpace + ":100002";

    /// <summary>
    /// 第三方登录client_id为空
    /// </summary>
    public const string ErrorCode100003 = NameSpace + ":100003";

    /// <summary>
    /// 第三方登录client_secret为空
    /// </summary>
    public const string ErrorCode100004 = NameSpace + ":100004";

    /// <summary>
    /// 第三方登录获取access_token失败
    /// </summary>
    public const string ErrorCode100005 = NameSpace + ":100005";

    /// <summary>
    /// 第三方登录client_name为空
    /// </summary>
    public const string ErrorCode100006 = NameSpace + ":100006";

    /// <summary>
    /// 第三方登录获取用户信息失败
    /// </summary>
    public const string ErrorCode100007 = NameSpace + ":100007";

    /// <summary>
    /// 第三方登录RedirectUrl为空
    /// </summary>
    public const string ErrorCode100008 = NameSpace + ":100008";
}
