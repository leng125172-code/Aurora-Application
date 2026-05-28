using AuroraStruct3D.Hubs;
using AuroraStruct3D.Ktech;
using AuroraStruct3D.Ktech.Dtos;
using Microsoft.AspNetCore.SignalR;
using Volo.Abp.DependencyInjection;

namespace AuroraStruct3D.Notifiers;

/// <summary>
/// 通过 SignalR Hub 推送瓴控 KTECH 实时数据和升级进度的实现。
/// </summary>
[ExposeServices(typeof(IKtechMotorNotifier))]
public class SignalRKtechMotorNotifier : IKtechMotorNotifier, ISingletonDependency
{
    private readonly IHubContext<KtechMotorHub, IKtechMotorHub> _hubContext;

    public SignalRKtechMotorNotifier(IHubContext<KtechMotorHub, IKtechMotorHub> hubContext)
    {
        _hubContext = hubContext;
    }

    /// <inheritdoc/>
    public Task NotifyStateAsync(KtechStateSnapshotDto state) =>
        _hubContext.Clients.All.ReceiveKtechMotorStateAsync(state);

    /// <inheritdoc/>
    public Task NotifyUpgradeProgressAsync(KtechUpgradeProgressDto progress) =>
        _hubContext.Clients.All.ReceiveKtechUpgradeProgressAsync(progress);
}
