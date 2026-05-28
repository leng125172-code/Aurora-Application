namespace AuroraStruct3D.ProductModels;

/// <summary>
/// 产品数模转换进度通知器接口。
/// 由 HttpApi.Host 层通过 SignalR Hub 实现，用于向前端实时推送转换开始/完成事件。
/// </summary>
public interface IProductModelConversionNotifier
{
    /// <summary>
    /// 通知前端指定数模已开始转换。
    /// </summary>
    /// <param name="productModelId">数模 ID</param>
    Task NotifyStartedAsync(Guid productModelId);

    /// <summary>
    /// 通知前端指定数模转换已完成（成功或失败）。
    /// </summary>
    /// <param name="productModelId">数模 ID</param>
    /// <param name="success">是否成功</param>
    /// <param name="errorMessage">失败时的错误信息（成功时为 null）</param>
    Task NotifyFinishedAsync(Guid productModelId, bool success, string? errorMessage);
}
