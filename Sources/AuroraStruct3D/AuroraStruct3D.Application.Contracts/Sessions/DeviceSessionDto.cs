namespace AuroraStruct3D.Sessions;

/// <summary>设备独占操作会话的数据传输对象</summary>
public sealed class DeviceSessionDto
{
    /// <summary>设备 ID</summary>
    public Guid DeviceId { get; set; }

    /// <summary>设备类型</summary>
    public DeviceType DeviceType { get; set; }

    /// <summary>占用者的客户端会话 ID（per-tab UUID）</summary>
    public string ClientSessionId { get; set; } = string.Empty;

    /// <summary>占用者用户 ID（可为 null）</summary>
    public string? OccupantUserId { get; set; }

    /// <summary>占用者用户名（用于 UI 显示）</summary>
    public string OccupantUserName { get; set; } = string.Empty;

    /// <summary>会话获取时间（UTC）</summary>
    public DateTimeOffset AcquiredAt { get; set; }

    /// <summary>过期时间（UTC）；null 表示无限期（预览会话）</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
}
