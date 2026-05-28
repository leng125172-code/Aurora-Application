using AuroraStruct3D.Motors.Dtos;
using Volo.Abp.DependencyInjection;

namespace AuroraStruct3D.Motors;

/// <summary>
/// 电机扫描进度通知器的空实现，作为默认占位，避免未注册具体推送器时构造失败。
/// 标记 <see cref="DependencyAttribute.TryRegister"/>，仅在无其它实现注册时才会生效；
/// HttpApi.Host 启动时会自动注册基于 SignalR 的实现并覆盖此默认。
/// </summary>
[Dependency(TryRegister = true)]
[ExposeServices(typeof(IMotorScanProgressNotifier))]
public class NullMotorScanProgressNotifier : IMotorScanProgressNotifier, ISingletonDependency
{
    /// <inheritdoc/>
    public Task NotifyAsync(MotorScanProgressDto progress)
    {
        // 空实现，不做任何推送
        return Task.CompletedTask;
    }
}
