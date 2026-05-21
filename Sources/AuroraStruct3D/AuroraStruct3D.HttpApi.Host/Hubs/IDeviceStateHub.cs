using AuroraStruct3D.DeviceState;

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
}
