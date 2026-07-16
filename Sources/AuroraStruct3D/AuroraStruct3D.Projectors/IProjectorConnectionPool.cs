namespace AuroraStruct3D.Projectors;

/// <summary>
/// 投影仪连接池接口。
/// 管理多台投影仪设备的独立连接实例，每台设备一个 <see cref="IDlpProjectorService"/> 实例。
/// 注册为 singleton，在应用生命周期内持久维护长连接。
/// </summary>
public interface IProjectorConnectionPool
{
    /// <summary>
    /// 获取或创建指定设备 ID 对应的投影仪服务实例。
    /// 若实例不存在则新建，若已存在则直接返回。
    /// </summary>
    /// <param name="deviceId">数据库中的 ProjectorDevice.Id</param>
    IDlpProjectorService GetOrCreate(Guid deviceId);

    /// <summary>
    /// 获取已存在的投影仪服务实例，若不存在返回 null
    /// </summary>
    IDlpProjectorService? TryGet(Guid deviceId);

    /// <summary>
    /// 断开并移除指定设备的连接实例
    /// </summary>
    Task RemoveAsync(Guid deviceId);

    /// <summary>
    /// 获取所有已注册设备 ID 列表（包含已断开的）
    /// </summary>
    IReadOnlyList<Guid> GetAllDeviceIds();
}
