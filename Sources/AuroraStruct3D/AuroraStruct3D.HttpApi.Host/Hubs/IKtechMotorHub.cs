using AuroraStruct3D.Ktech.Dtos;

namespace AuroraStruct3D.Hubs;

/// <summary>
/// 瓴控 KTECH 电机操作台 SignalR Hub 客户端推送接口。
/// </summary>
public interface IKtechMotorHub
{
    /// <summary>推送一次实时状态快照。</summary>
    Task ReceiveKtechMotorStateAsync(KtechStateSnapshotDto state);

    /// <summary>推送一次固件升级进度。</summary>
    Task ReceiveKtechUpgradeProgressAsync(KtechUpgradeProgressDto progress);
}
