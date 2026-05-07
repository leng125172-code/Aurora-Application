namespace Lion.AbpPro.Oidc.WorkWechat;

public class GetWorkWechatUserDetailResponse
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
    /// 用户ID
    /// </summary>
    public string userid { get; set; }

    /// <summary>
    /// 姓名
    /// </summary>
    public string name { get; set; }

    /// <summary>
    /// 手机号
    /// </summary>
    public string mobile { get; set; }

    /// <summary>
    /// 邮箱
    /// </summary>
    public string email { get; set; }

    /// <summary>
    /// 企业邮箱，仅在用户同意snsapi_privateinfo授权时返回，第三方应用不可获取
    /// </summary>
    public string biz_mail { get; set; }

    /// <summary>
    /// 头像URL
    /// </summary>
    public string avatar { get; set; }
}
