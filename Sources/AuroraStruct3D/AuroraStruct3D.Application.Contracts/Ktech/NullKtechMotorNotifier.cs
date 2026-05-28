using AuroraStruct3D.Ktech.Dtos;
using Volo.Abp.DependencyInjection;

namespace AuroraStruct3D.Ktech;

/// <summary>
/// 默认空实现，未启用 SignalR（如单元测试场景）时使用。
/// 在 HttpApi.Host 中会被 SignalR 实现覆盖。
/// </summary>
[Dependency(TryRegister = true)]
[ExposeServices(typeof(IKtechMotorNotifier))]
public class NullKtechMotorNotifier : IKtechMotorNotifier, ISingletonDependency
{
    /// <inheritdoc/>
    public Task NotifyStateAsync(KtechStateSnapshotDto state) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task NotifyUpgradeProgressAsync(KtechUpgradeProgressDto progress) => Task.CompletedTask;
}
