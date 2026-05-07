namespace Lion.AbpPro.BasicManagement.Users.Dtos;

public class PagingOnlineUserInput : PagingBase
{
    /// <summary>
    /// 用户名(支持模糊匹配)
    /// </summary>
    public string UserName { get; set; }
}
