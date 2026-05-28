namespace AuroraStruct3D.Sessions;

/// <summary>
/// 设备独占操作会话管理器。
/// 以 per-device Guid 为 key 在内存中维护软独占会话；会话变更通过 <see cref="SessionChanged"/> 事件广播。
/// </summary>
public interface IDeviceOperationSessionManager
{
    /// <summary>
    /// 会话发生变更时触发（Acquired / Released / ForceTaken）。
    /// 由 DeviceSessionBroadcaster 订阅并转发至 SignalR 客户端。
    /// </summary>
    event Action<DeviceSessionChangedDto>? SessionChanged;

    /// <summary>
    /// 尝试为指定设备获取独占会话。
    /// <list type="bullet">
    ///   <item>设备无人占用 → 创建新会话并返回。</item>
    ///   <item>已被同 clientSessionId 占用 → 续期并返回（同标签页重复调用安全）。</item>
    ///   <item>已被不同 clientSessionId 占用且 force=false → 抛出 <see cref="DeviceOccupiedException"/>。</item>
    ///   <item>已被不同 clientSessionId 占用且 force=true → 踢出旧会话，创建新会话并返回。</item>
    /// </list>
    /// </summary>
    /// <param name="deviceId">设备 ID</param>
    /// <param name="deviceType">设备类型</param>
    /// <param name="clientSessionId">调用方的客户端会话 ID（per-tab UUID）</param>
    /// <param name="userId">调用方用户 ID（可为 null；仅用于显示）</param>
    /// <param name="userName">调用方用户名（用于 UI 显示）</param>
    /// <param name="force">是否强制接管</param>
    /// <param name="neverExpire">true 表示无限期会话（预览）；false 使用默认 60s TTL</param>
    DeviceSessionDto TryAcquire(
        Guid deviceId,
        DeviceType deviceType,
        string clientSessionId,
        string? userId,
        string userName,
        bool force = false,
        bool neverExpire = false
    );

    /// <summary>
    /// 释放指定设备的会话。
    /// </summary>
    /// <param name="deviceId">设备 ID</param>
    /// <param name="clientSessionId">
    /// 调用方的客户端会话 ID；仅当 clientSessionId 匹配时释放。
    /// 传入 null 时强制释放（供系统调用，如预览停止、超时清理）。
    /// </param>
    void Release(Guid deviceId, string? clientSessionId);

    /// <summary>
    /// 续期指定设备的会话过期时间（TTL 重置为 60s）。
    /// 若会话为无限期（预览），则不修改。若 clientSessionId 不匹配，则不操作（静默忽略）。
    /// </summary>
    void RenewExpiry(Guid deviceId, string clientSessionId);

    /// <summary>获取指定设备的当前会话；设备未被占用时返回 null</summary>
    DeviceSessionDto? GetSession(Guid deviceId);

    /// <summary>获取所有当前活跃会话列表</summary>
    IReadOnlyList<DeviceSessionDto> GetAllSessions();
}
