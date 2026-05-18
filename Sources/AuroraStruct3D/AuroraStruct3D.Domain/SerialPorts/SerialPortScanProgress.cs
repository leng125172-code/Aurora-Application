namespace AuroraStruct3D.SerialPorts;

/// <summary>
/// 扫描串口设备的进度上报值对象。
/// 在 <see cref="ISerialPortScanService.ScanForDevicesAsync"/> 扫描过程中，
/// 通过 <see cref="IProgress{T}"/> 回调逐步上报当前进度。
/// </summary>
public sealed class SerialPortScanProgress
{
    /// <summary>正在扫描的系统串口名称（如 /dev/ttyS6、COM3）</summary>
    public string PortName { get; init; } = string.Empty;

    /// <summary>当前正在探测的从机地址</summary>
    public int CurrentSlaveId { get; init; }

    /// <summary>本次扫描地址总数（扫描范围：1 ~ TotalSlaves）</summary>
    public int TotalSlaves { get; init; }

    /// <summary>截至当前已发现响应的从机地址列表（快照，每次上报时追加）</summary>
    public IReadOnlyList<int> DiscoveredSlaveIds { get; init; } = [];

    /// <summary>扫描完成进度百分比（0.0 ~ 1.0）</summary>
    public double Progress =>
        TotalSlaves > 0 ? (double)CurrentSlaveId / TotalSlaves : 0;
}
