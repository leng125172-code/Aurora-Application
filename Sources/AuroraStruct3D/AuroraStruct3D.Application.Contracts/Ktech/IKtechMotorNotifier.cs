using AuroraStruct3D.Ktech.Dtos;

namespace AuroraStruct3D.Ktech;

/// <summary>
/// 瓴控 KTECH 实时数据推送通知器（由 HttpApi.Host 通过 SignalR Hub 实现）。
/// </summary>
public interface IKtechMotorNotifier
{
    /// <summary>推送一次状态快照。</summary>
    Task NotifyStateAsync(KtechStateSnapshotDto state);

    /// <summary>推送一次升级进度。</summary>
    Task NotifyUpgradeProgressAsync(KtechUpgradeProgressDto progress);
}
