namespace AuroraStruct3D.Sessions;

/// <summary>
/// 设备占用信息，描述当前持有该设备独占操作会话的客户端。
/// 纯内存对象，不持久化。
/// </summary>
/// <param name="DeviceId">设备 ID</param>
/// <param name="DeviceType">设备类型</param>
/// <param name="ClientSessionId">
/// 浏览器标签页会话 ID（per-tab UUID，由前端存于 sessionStorage）。
/// 作为 ownership key：同标签页刷新后相同，不同标签页或不同主机则不同。
/// </param>
/// <param name="OccupantUserId">占用者用户 ID（可为 null；仅用于显示）</param>
/// <param name="OccupantUserName">占用者用户名（用于 UI 显示）</param>
/// <param name="AcquiredAt">会话获取时间（UTC）</param>
/// <param name="ExpiresAt">
/// 过期时间（UTC）；null 表示无限期（相机预览会话），有值表示手动操作会话（默认 60 s TTL）。
/// </param>
public sealed record DeviceOccupantInfo(
    Guid DeviceId,
    DeviceType DeviceType,
    string ClientSessionId,
    string? OccupantUserId,
    string OccupantUserName,
    DateTimeOffset AcquiredAt,
    DateTimeOffset? ExpiresAt
);
