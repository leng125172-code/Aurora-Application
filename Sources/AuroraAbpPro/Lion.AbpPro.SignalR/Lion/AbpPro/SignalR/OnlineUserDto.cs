namespace Lion.AbpPro.SignalR;

public class OnlineUserDto
{
    /// <summary>
    /// 客户端连接Id
    /// </summary>
    public string ConnectionId { get; set; }

    /// <summary>
    /// 用户id
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 用户名
    /// </summary>
    public string UserName { get; set; }

    /// <summary>
    /// 登录时间
    /// </summary>
    public DateTime LoginTime { get; set; }

    /// <summary>
    /// 登录ip
    /// </summary>
    public string Ip { get; set; }

    /// <summary>
    /// 设备信息
    /// </summary>
    public string DeviceInfo { get; set; }
}
