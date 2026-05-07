using Lion.AbpPro.BasicManagement.Users.Dtos;
using Lion.AbpPro.SignalR;
using Lion.AbpPro.SignalR.Hubs;
using Mapster;
using Microsoft.AspNetCore.SignalR;

namespace Lion.AbpPro.BasicManagement.Users;

[Authorize(policy: BasicManagementPermissions.SystemManagement.OnlineManagement)]
public class OnlineAppService : BasicManagementAppService, IOnlineAppService
{
    private readonly IHubContext<NotificationHub, INotificationHub> _hubContext;

    public OnlineAppService(IHubContext<NotificationHub, INotificationHub> hubContext)
    {
        _hubContext = hubContext;
    }

    /// <summary>
    /// 分页获取在线用户
    /// </summary>
    public async Task<PagedResultDto<PageOnlineUserOutput>> PageAsync(PagingOnlineUserInput input)
    {
        var dataWhere = NotificationHub
            .OnlineUsers.Values.AsEnumerable()
            .WhereIf(
                input.UserName.IsNotNullOrWhiteSpace(),
                u => u.UserName.Contains(input.UserName)
            );

        var filteredList = dataWhere.ToList();

        return new PagedResultDto<PageOnlineUserOutput>
        {
            TotalCount = filteredList.Count,
            Items = filteredList
                .OrderByDescending(e => e.LoginTime)
                .Adapt<List<PageOnlineUserOutput>>(),
        };
    }

    /// <summary>
    /// 强制下线
    /// </summary>
    [Authorize(policy: BasicManagementPermissions.SystemManagement.OnlineManagementForceOut)]
    public async Task ForceOutAsync(IdInput input)
    {
        // 获取该用户的所有连接
        var userConnections = NotificationHub
            .OnlineUsers.Values.Where(e => e.UserId == input.Id)
            .Select(e => e.ConnectionId)
            .ToList();

        if (userConnections.Any())
        {
            // 通知该用户的所有连接强制下线
            await _hubContext
                .Clients.Users([input.Id.ToString()])
                .ForceOutAsync(new ForceOutDto("您已被强制下线."));

            // 移除该用户的所有连接记录
            foreach (var connectionId in userConnections)
            {
                NotificationHub.OnlineUsers.TryRemove(connectionId, out _);
            }
        }
    }
}
