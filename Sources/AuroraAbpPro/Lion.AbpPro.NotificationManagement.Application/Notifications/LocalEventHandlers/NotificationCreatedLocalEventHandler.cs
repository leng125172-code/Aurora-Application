using Lion.AbpPro.SignalR.LocalEvent.Notification;
using Volo.Abp.MultiTenancy;

namespace Lion.AbpPro.NotificationManagement.Notifications.LocalEventHandlers
{
    /// <summary>
    /// 创建消息事件处理
    /// </summary>
    public class NotificationCreatedLocalEventHandler
        : ILocalEventHandler<CreatedNotificationLocalEvent>,
            ITransientDependency
    {
        private readonly INotificationManager _notificationManager;
        private readonly CurrentTenant _currentTenant;

        public NotificationCreatedLocalEventHandler(
            INotificationManager notificationManager,
            CurrentTenant currentTenant
        )
        {
            _notificationManager = notificationManager;
            _currentTenant = currentTenant;
        }

        public virtual async Task HandleEventAsync(CreatedNotificationLocalEvent eventData)
        {
            using (_currentTenant.Change(eventData.TenantId))
            {
                await _notificationManager.CreateAsync(
                    eventData.Id,
                    eventData.Title,
                    eventData.Content,
                    eventData.MessageType,
                    eventData.MessageLevel,
                    eventData.SenderUserId,
                    eventData.SenderUserName,
                    eventData.ReceiveUserId,
                    eventData.ReceiveUserName
                );
            }
        }
    }
}
