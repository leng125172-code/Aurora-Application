namespace AuroraStruct3D.Hubs;

/// <summary>
/// 产品数模转换进度 SignalR Hub 的客户端推送接口。
/// </summary>
public interface IProductModelHub
{
    /// <summary>
    /// 通知客户端指定数模开始转换。
    /// </summary>
    /// <param name="productModelId">数模 ID</param>
    Task ReceiveConversionStartedAsync(Guid productModelId);

    /// <summary>
    /// 通知客户端指定数模转换已完成（成功或失败）。
    /// </summary>
    /// <param name="productModelId">数模 ID</param>
    /// <param name="success">是否成功</param>
    /// <param name="errorMessage">失败时的错误信息（成功时为 null）</param>
    Task ReceiveConversionFinishedAsync(Guid productModelId, bool success, string? errorMessage);
}
