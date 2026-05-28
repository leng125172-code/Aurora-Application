using AuroraStruct3D.DeviceState;
using AuroraStruct3D.Sessions;

namespace AuroraStruct3D.Hubs;

/// <summary>
/// 设备状态 SignalR Hub 的客户端推送接口
/// </summary>
public interface IDeviceStateHub
{
    /// <summary>推送设备状态快照给客户端</summary>
    Task ReceiveDeviceStateAsync(DeviceStateDto state);

    /// <summary>推送当前活跃故障（无故障时为 null）给客户端</summary>
    Task ReceiveDeviceFaultAsync(DeviceFaultDto? fault);

    /// <summary>推送设备操作会话变更通知（Acquired / Released / ForceTaken）给所有客户端</summary>
    Task ReceiveDeviceSessionChangedAsync(DeviceSessionChangedDto changed);

    /// <summary>推送所有当前活跃设备操作会话列表（客户端首次连接时接收）</summary>
    Task ReceiveAllDeviceSessionsAsync(IReadOnlyList<DeviceSessionDto> sessions);
}
