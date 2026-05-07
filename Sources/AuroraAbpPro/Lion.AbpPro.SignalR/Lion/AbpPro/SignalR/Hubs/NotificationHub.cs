using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.AspNetCore.SignalR;
using Volo.Abp.AspNetCore.WebClientInfo;
using Volo.Abp.Auditing;
using Volo.Abp.Timing;

namespace Lion.AbpPro.SignalR.Hubs
{
    [HubRoute("SignalR/Notification")]
    [Authorize]
    [DisableAuditing]
    public class NotificationHub : AbpHub<INotificationHub>
    {
        public static ConcurrentDictionary<string, OnlineUserDto> OnlineUsers { get; set; } = new();

        private readonly IWebClientInfoProvider _webClientInfoProvider;
        private readonly ILogger<NotificationHub> _logger;
        private readonly IClock _clock;

        public NotificationHub(
            IWebClientInfoProvider webClientInfoProvider,
            ILogger<NotificationHub> logger,
            IClock clock
        )
        {
            _webClientInfoProvider = webClientInfoProvider;
            _logger = logger;
            _clock = clock;
        }

        public override async Task OnConnectedAsync()
        {
            if (CurrentUser.IsAuthenticated)
            {
                var user = new OnlineUserDto()
                {
                    UserId = CurrentUser.GetId(),
                    UserName = CurrentUser.UserName,
                    LoginTime = _clock.Now,
                    Ip = _webClientInfoProvider.ClientIpAddress,
                    DeviceInfo = _webClientInfoProvider.DeviceInfo,
                    ConnectionId = Context.ConnectionId,
                };

                // 只添加/更新当前连接,支持同一用户多连接(多标签页/多设备)
                OnlineUsers.AddOrUpdate(Context.ConnectionId, user, (_, _) => user);

                _logger.LogDebug(
                    $"{_clock.Now}：{CurrentUser.Name},{Context.ConnectionId}连接服务端success，当前已连接{OnlineUsers.Count}个"
                );
            }

            await base.OnConnectedAsync();
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public override Task OnDisconnectedAsync(Exception exception)
        {
            if (CurrentUser.IsAuthenticated)
            {
                // 只移除当前ConnectionId,不影响同一用户的其他连接
                OnlineUsers.Remove(Context.ConnectionId, out _);
                _logger.LogDebug(
                    $"{_clock.Now}:用户{CurrentUser.UserName}离开了，当前已连接{OnlineUsers.Count}个"
                );
            }
            return base.OnDisconnectedAsync(exception);
        }
    }
}
