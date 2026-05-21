namespace AuroraStruct3D.DeviceState;

/// <summary>
/// 设备当前状态快照 DTO，用于 REST 接口和 SignalR 推送
/// </summary>
public class DeviceStateDto
{
    /// <summary>当前设备总状态</summary>
    public DeviceStatus Status { get; set; }

    /// <summary>当前运行模式</summary>
    public DeviceRunMode RunMode { get; set; }

    /// <summary>当前活跃故障记录 ID（无故障时为 null）</summary>
    public Guid? CurrentFaultId { get; set; }

    /// <summary>当前故障等级（无故障时为 null）</summary>
    public DeviceFaultLevel? CurrentFaultLevel { get; set; }

    /// <summary>当前故障码（无故障时为 null）</summary>
    public string? CurrentFaultCode { get; set; }

    /// <summary>设备是否处于过渡状态</summary>
    public bool IsInTransition { get; set; }

    /// <summary>设备是否可接收生产指令</summary>
    public bool CanAcceptProductionCommand { get; set; }

    /// <summary>设备是否可切换运行模式</summary>
    public bool CanSwitchMode { get; set; }
}
