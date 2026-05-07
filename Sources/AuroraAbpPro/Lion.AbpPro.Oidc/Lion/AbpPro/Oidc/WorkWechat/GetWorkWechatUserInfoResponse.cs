namespace Lion.AbpPro.Oidc.WorkWechat;

public class GetWorkWechatUserInfoResponse
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
    public string UserId { get; set; }

    /// <summary>
    /// 用户票据
    /// </summary>
    public string user_ticket { get; set; }

    /// <summary>
    /// 设备ID
    /// </summary>
    public string DeviceId { get; set; }

    /// <summary>
    /// 外部联系人OpenId
    /// </summary>
    public string OpenId { get; set; }
}
