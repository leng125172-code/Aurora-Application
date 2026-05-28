namespace AuroraStruct3D.Sessions;

/// <summary>设备独占操作会话管理 AppService 接口</summary>
public interface IDeviceSessionAppService : IApplicationService
{
    /// <summary>
    /// 获取所有当前活跃的设备占用会话
    /// </summary>
    Task<List<DeviceSessionDto>> GetAllAsync();

    /// <summary>
    /// 查询指定设备的当前占用会话；未被占用时返回 null
    /// </summary>
    Task<DeviceSessionDto?> GetAsync(Guid id);

    /// <summary>
    /// 为当前客户端标签页获取指定设备的独占操作权（非强制）。
    /// 若设备已被其他标签页/主机占用，抛出 <see cref="DeviceOccupiedException"/>（HTTP 409）。
    /// </summary>
    Task<DeviceSessionDto> AcquireAsync(Guid deviceId, DeviceType deviceType);

    /// <summary>
    /// 强制接管指定设备的独占操作权（会踢出当前占用者）。
    /// 若设备为相机且当前处于预览状态，先停止预览再接管。
    /// </summary>
    Task<DeviceSessionDto> ForceAcquireAsync(Guid deviceId, DeviceType deviceType);

    /// <summary>
    /// 释放当前客户端标签页持有的指定设备操作权。
    /// 非 owner 调用时静默忽略。
    /// </summary>
    Task ReleaseAsync(Guid deviceId);
}
