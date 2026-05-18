namespace AuroraStruct3D.SerialPorts;

/// <summary>
/// 串口设备扫描服务接口。
/// 用于枚举系统可用串口、并向指定串口发送探测帧以发现从机设备。
///
/// 注意：本接口已预留架构位置，具体实现将在后续版本提供。
/// 实现时需注意：RS485 半双工总线同一时刻只允许一个扫描任务占用串口。
/// </summary>
public interface ISerialPortScanService
{
    /// <summary>
    /// 枚举当前系统所有可用的串口名称列表。
    /// Windows 返回如 ["COM1","COM3"]；Linux 返回如 ["/dev/ttyS0","/dev/ttyUSB0"]。
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>系统串口名称列表</returns>
    Task<IReadOnlyList<string>> GetSystemPortNamesAsync(
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 对指定串口配置的地址范围发送协议探测帧，发现总线上响应的从机设备。
    /// 扫描过程中通过 <paramref name="progress"/> 实时上报进度。
    /// </summary>
    /// <param name="serialPortConfigId">目标串口配置 ID</param>
    /// <param name="startSlaveId">起始从机地址（包含，最小 1）</param>
    /// <param name="endSlaveId">结束从机地址（包含，最大 247）</param>
    /// <param name="progress">进度回调，可为 null</param>
    /// <param name="cancellationToken">取消令牌（可用于中途终止扫描）</param>
    /// <returns>已发现的从机地址列表</returns>
    Task<IReadOnlyList<int>> ScanForDevicesAsync(
        Guid serialPortConfigId,
        int startSlaveId = 1,
        int endSlaveId = 247,
        IProgress<SerialPortScanProgress>? progress = null,
        CancellationToken cancellationToken = default
    );
}
