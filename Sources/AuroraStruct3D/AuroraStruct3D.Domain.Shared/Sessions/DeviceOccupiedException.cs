namespace AuroraStruct3D.Sessions;

/// <summary>
/// 当尝试获取设备独占会话，但设备已被其他客户端（不同 clientSessionId）占用时抛出此异常。
/// ABP 将其序列化为 HTTP 409 Conflict（需在 Module 中注册状态码映射）。
/// </summary>
public sealed class DeviceOccupiedException : BusinessException
{
    /// <summary>当前占用该设备的客户端信息</summary>
    public DeviceOccupantInfo Occupant { get; }

    /// <param name="occupant">当前占用者信息</param>
    public DeviceOccupiedException(DeviceOccupantInfo occupant)
        : base(
            code: AuroraStruct3DDomainErrorCodes.DeviceOccupied,
            message: $"设备正在被 [{occupant.OccupantUserName}] 使用，请等待或强制接管。"
        )
    {
        Occupant = occupant;
    }
}
