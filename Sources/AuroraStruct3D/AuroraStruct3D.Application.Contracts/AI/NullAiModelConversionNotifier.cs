using AuroraStruct3D.AI.Dtos;
using Volo.Abp.DependencyInjection;

namespace AuroraStruct3D.AI;

/// <summary>
/// AI 模型转换通知器空实现。
/// 未启用 SignalR 的场景下用于占位，避免依赖注入失败。
/// </summary>
[Dependency(TryRegister = true)]
[ExposeServices(typeof(IAiModelConversionNotifier))]
public class NullAiModelConversionNotifier : IAiModelConversionNotifier, ISingletonDependency
{
    /// <inheritdoc/>
    public Task NotifyQueuedAsync(AiModelConversionStateDto state) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task NotifyStartedAsync(AiModelConversionStateDto state) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task NotifyFinishedAsync(AiModelConversionStateDto state) => Task.CompletedTask;
}
