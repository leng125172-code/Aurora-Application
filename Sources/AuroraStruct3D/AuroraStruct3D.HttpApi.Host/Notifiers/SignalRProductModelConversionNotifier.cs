using AuroraStruct3D.Hubs;
using AuroraStruct3D.ProductModels;
using Microsoft.AspNetCore.SignalR;
using Volo.Abp.DependencyInjection;

namespace AuroraStruct3D.Notifiers;

/// <summary>
/// 通过 SignalR Hub 推送产品数模转换进度通知的具体实现。
/// </summary>
[ExposeServices(typeof(IProductModelConversionNotifier))]
public class SignalRProductModelConversionNotifier
    : IProductModelConversionNotifier,
        ISingletonDependency
{
    private readonly IHubContext<ProductModelHub, IProductModelHub> _hubContext;

    public SignalRProductModelConversionNotifier(
        IHubContext<ProductModelHub, IProductModelHub> hubContext
    )
    {
        _hubContext = hubContext;
    }

    /// <inheritdoc/>
    public Task NotifyStartedAsync(Guid productModelId)
    {
        return _hubContext.Clients.All.ReceiveConversionStartedAsync(productModelId);
    }

    /// <inheritdoc/>
    public Task NotifyFinishedAsync(Guid productModelId, bool success, string? errorMessage)
    {
        return _hubContext.Clients.All.ReceiveConversionFinishedAsync(
            productModelId,
            success,
            errorMessage
        );
    }
}
