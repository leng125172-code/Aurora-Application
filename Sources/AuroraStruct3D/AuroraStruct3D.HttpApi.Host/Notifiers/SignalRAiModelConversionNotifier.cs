using AuroraStruct3D.AI;
using AuroraStruct3D.AI.Dtos;
using AuroraStruct3D.Hubs;
using Microsoft.AspNetCore.SignalR;
using Volo.Abp.DependencyInjection;

namespace AuroraStruct3D.Notifiers;

/// <summary>
/// 通过 SignalR Hub 推送 AI 模型转换阶段通知的实现。
/// </summary>
[ExposeServices(typeof(IAiModelConversionNotifier))]
public class SignalRAiModelConversionNotifier : IAiModelConversionNotifier, ISingletonDependency
{
    private readonly IHubContext<AiModelConversionHub, IAiModelConversionHub> _hubContext;

    public SignalRAiModelConversionNotifier(
        IHubContext<AiModelConversionHub, IAiModelConversionHub> hubContext
    )
    {
        _hubContext = hubContext;
    }

    /// <inheritdoc/>
    public Task NotifyQueuedAsync(AiModelConversionStateDto state)
    {
        return _hubContext.Clients.All.ReceiveConversionQueuedAsync(state);
    }

    /// <inheritdoc/>
    public Task NotifyStartedAsync(AiModelConversionStateDto state)
    {
        return _hubContext.Clients.All.ReceiveConversionStartedAsync(state);
    }

    /// <inheritdoc/>
    public Task NotifyFinishedAsync(AiModelConversionStateDto state)
    {
        return _hubContext.Clients.All.ReceiveConversionFinishedAsync(state);
    }
}
