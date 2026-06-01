using AuroraStruct3D.CalibrationManagement;
using AuroraStruct3D.Hubs;
using Microsoft.AspNetCore.SignalR;
using Volo.Abp.DependencyInjection;

namespace AuroraStruct3D.Notifiers;

/// <summary>
/// 通过 SignalR CalibrationHub 推送标定计算进度通知的具体实现。
/// 向所有已连接客户端广播进度与完成事件。
/// </summary>
[ExposeServices(typeof(ICalibrationProgressNotifier))]
public class SignalRCalibrationProgressNotifier
    : ICalibrationProgressNotifier,
        ISingletonDependency
{
    private readonly IHubContext<CalibrationHub, ICalibrationHub> _hubContext;

    public SignalRCalibrationProgressNotifier(
        IHubContext<CalibrationHub, ICalibrationHub> hubContext
    )
    {
        _hubContext = hubContext;
    }

    /// <inheritdoc/>
    public Task NotifyProgressAsync(
        Guid projectId,
        string stage,
        int percent,
        string? message = null
    )
    {
        return _hubContext.Clients.All.ReceiveCalibrationProgressAsync(
            projectId,
            stage,
            percent,
            message
        );
    }

    /// <inheritdoc/>
    public Task NotifyCompletedAsync(
        Guid projectId,
        bool success,
        Guid? resultId = null,
        string? errorMessage = null
    )
    {
        return _hubContext.Clients.All.ReceiveCalibrationCompletedAsync(
            projectId,
            success,
            resultId,
            errorMessage
        );
    }
}
