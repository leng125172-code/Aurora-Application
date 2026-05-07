using Lion.AbpPro.BasicManagement.Users.Dtos;

namespace Lion.AbpPro.BasicManagement.Users;

public interface IOnlineAppService : IApplicationService
{
    /// <summary>
    /// 分页获取在线用户
    /// </summary>
    Task<PagedResultDto<PageOnlineUserOutput>> PageAsync(PagingOnlineUserInput input);

    /// <summary>
    /// 强制下线
    /// </summary>
    Task ForceOutAsync(IdInput input);
}
