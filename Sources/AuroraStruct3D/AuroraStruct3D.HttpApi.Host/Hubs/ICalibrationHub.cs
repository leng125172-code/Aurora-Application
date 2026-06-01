namespace AuroraStruct3D.Hubs;

/// <summary>
/// 标定计算进度推送 SignalR Hub 的客户端接口。
/// 客户端需实现此接口以接收服务端主动推送的事件。
/// </summary>
public interface ICalibrationHub
{
    /// <summary>
    /// 推送标定计算阶段进度。
    /// </summary>
    /// <param name="projectId">标定工程 Id</param>
    /// <param name="stage">当前阶段名称（intrinsics / extrinsics / structured-light / saving）</param>
    /// <param name="percent">0～100 的整数进度百分比</param>
    /// <param name="message">可选的中文状态说明</param>
    Task ReceiveCalibrationProgressAsync(
        Guid projectId,
        string stage,
        int percent,
        string? message
    );

    /// <summary>
    /// 推送标定计算完成通知（成功或失败）。
    /// </summary>
    /// <param name="projectId">标定工程 Id</param>
    /// <param name="success">是否成功</param>
    /// <param name="resultId">成功时写入的标定结果 Id；失败时为 null</param>
    /// <param name="errorMessage">失败时的错误信息；成功时为 null</param>
    Task ReceiveCalibrationCompletedAsync(
        Guid projectId,
        bool success,
        Guid? resultId,
        string? errorMessage
    );
}
