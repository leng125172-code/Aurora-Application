using AuroraStruct3D.Hubs;
using AuroraStruct3D.Leisai;
using AuroraStruct3D.Leisai.Dtos;
using Microsoft.AspNetCore.SignalR;
using Volo.Abp.DependencyInjection;

namespace AuroraStruct3D.Notifiers;

/// <summary>
/// 通过 SignalR Hub 推送雷赛 iCL-RS 实时数据的实现。
/// </summary>
[ExposeServices(typeof(ILeisaiMotorNotifier))]
public class SignalRLeisaiMotorNotifier : ILeisaiMotorNotifier, ISingletonDependency
{
    private readonly IHubContext<LeisaiMotorHub, ILeisaiMotorHub> _hubContext;

    public SignalRLeisaiMotorNotifier(IHubContext<LeisaiMotorHub, ILeisaiMotorHub> hubContext)
    {
        _hubContext = hubContext;
    }

    /// <inheritdoc/>
    public Task NotifyStateAsync(LeisaiStateSnapshotDto state) =>
        _hubContext.Clients.All.ReceiveLeisaiMotorStateAsync(state);

    /// <inheritdoc/>
    public Task NotifyTraceAsync(LeisaiTraceDto trace) =>
        _hubContext.Clients.All.ReceiveLeisaiTraceAsync(trace);
}
