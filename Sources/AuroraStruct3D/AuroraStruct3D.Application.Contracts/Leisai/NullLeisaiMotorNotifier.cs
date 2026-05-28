using AuroraStruct3D.Leisai.Dtos;
using Volo.Abp.DependencyInjection;

namespace AuroraStruct3D.Leisai;

/// <summary>
/// 雷赛通知器默认空实现，未启用 SignalR（如单元测试场景）时使用。
/// 在 HttpApi.Host 中会被 SignalR 实现覆盖。
/// </summary>
[Dependency(TryRegister = true)]
[ExposeServices(typeof(ILeisaiMotorNotifier))]
public class NullLeisaiMotorNotifier : ILeisaiMotorNotifier, ISingletonDependency
{
    /// <inheritdoc/>
    public Task NotifyStateAsync(LeisaiStateSnapshotDto state) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task NotifyTraceAsync(LeisaiTraceDto trace) => Task.CompletedTask;
}
