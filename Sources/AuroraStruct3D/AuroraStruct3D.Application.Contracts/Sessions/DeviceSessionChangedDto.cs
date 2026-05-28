namespace AuroraStruct3D.Sessions;

/// <summary>设备操作会话变更通知，通过 SignalR 推送给所有客户端</summary>
public sealed class DeviceSessionChangedDto
{
    /// <summary>变更动作</summary>
    public DeviceSessionAction Action { get; set; }

    /// <summary>新的占用者信息（Action 为 Released 时为 null）</summary>
    public DeviceSessionDto? NewOccupant { get; set; }

    /// <summary>被替换的旧占用者信息（Action 为 Acquired 且设备原本无人时为 null）</summary>
    public DeviceSessionDto? OldOccupant { get; set; }
}

/// <summary>会话变更类型</summary>
public enum DeviceSessionAction
{
    /// <summary>设备首次被占用</summary>
    Acquired,

    /// <summary>设备被主动释放或超时自动释放</summary>
    Released,

    /// <summary>设备被其他用户强制接管</summary>
    ForceTaken,
}
